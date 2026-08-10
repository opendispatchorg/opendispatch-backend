using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Results;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>
/// The request pipeline, exercised end to end through a real container, a real mediator and a
/// sample handler.
/// </summary>
/// <remarks>
/// <para>
/// What is under test is the composition, not the three behaviors one at a time: that a
/// malformed request never reaches a handler or a transaction, that a command's work is kept
/// only when the command succeeded, and that a bug takes the work with it. Each of those is a
/// statement about the order the behaviors run in, which is why they are asserted as one
/// sequence of what happened rather than as a set of "was this called" checks.
/// </para>
/// <para>
/// That a rollback actually removes rows is a claim about the database, and is proved against a
/// real one in <c>Api.IntegrationTests</c>.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class PipelineTests
{
    [Fact]
    public async Task ValidationFailureStopsBeforeTheHandlerAndBeforeAnyTransaction()
    {
        await using var pipeline = new SamplePipeline();

        var result = await pipeline.Sender.Send(new SampleCommand(string.Empty, SampleOutcome.Succeed));

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(ErrorCategory.Validation, error.Category);

        var failure = Assert.Single(error.Failures);
        Assert.Equal(nameof(SampleCommand.Name), failure.Key);
        Assert.Equal("A name is required.", Assert.Single(failure.Value));

        // Nothing at all happened: no handler, and — the part that matters — no transaction
        // opened for work that was never going to be kept.
        Assert.Empty(pipeline.Journal.Entries);
    }

    [Fact]
    public async Task ASuccessfulCommandIsSavedAndCommitted()
    {
        await using var pipeline = new SamplePipeline();

        var result = await pipeline.Sender.Send(new SampleCommand("Ada", SampleOutcome.Succeed));

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada", result.Value);
        Assert.Equal(
            [PipelineJournal.Begun, PipelineJournal.Handled, PipelineJournal.Saved, PipelineJournal.Committed],
            pipeline.Journal.Entries);
    }

    [Fact]
    public async Task AFailedCommandIsRolledBackAndNeverSaved()
    {
        await using var pipeline = new SamplePipeline();

        var result = await pipeline.Sender.Send(new SampleCommand("Ada", SampleOutcome.Fail));

        Assert.Equal(SampleCommandHandler.Refused, result.Error);

        // No save between the handler and the rollback: a command that failed is a command whose
        // work is not merely undone but never committed to in the first place.
        Assert.Equal(
            [PipelineJournal.Begun, PipelineJournal.Handled, PipelineJournal.RolledBack],
            pipeline.Journal.Entries);
    }

    [Fact]
    public async Task AThrownExceptionRollsBackAndIsNotTurnedIntoAResult()
    {
        await using var pipeline = new SamplePipeline();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.Sender.Send(new SampleCommand("Ada", SampleOutcome.Throw)));

        // A bug leaves as little behind as an expected failure does, and by the same mechanism:
        // the transaction ends without a commit.
        Assert.Equal(
            [PipelineJournal.Begun, PipelineJournal.Handled, PipelineJournal.RolledBack],
            pipeline.Journal.Entries);
    }

    [Fact]
    public async Task AQueryIsNeverGivenATransaction()
    {
        await using var pipeline = new SamplePipeline();

        var result = await pipeline.Sender.Send(new SampleQuery("Ada"));

        Assert.True(result.IsSuccess);
        Assert.Equal([PipelineJournal.Handled], pipeline.Journal.Entries);
    }

    /// <summary>
    /// Logging is outermost, and this is what says so: a validation failure is produced by a
    /// behavior the handler never sees, so it can only be logged by something wrapping it.
    /// </summary>
    [Fact]
    public async Task AFailureProducedByAnotherBehaviorIsStillLogged()
    {
        await using var pipeline = new SamplePipeline();

        await pipeline.Sender.Send(new SampleCommand(string.Empty, SampleOutcome.Succeed));

        var warning = Assert.Single(pipeline.Logs, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(nameof(SampleCommand), warning.Message, StringComparison.Ordinal);
        Assert.Contains(ValidationError.ValidationFailed, warning.Message, StringComparison.Ordinal);
    }
}
