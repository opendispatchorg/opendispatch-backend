using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Behaviors;
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

    /// <summary>
    /// The one exception the pipeline is allowed to answer for: a lost optimistic-concurrency
    /// race, which is what two dispatchers editing the same job produce and which was a 500 until
    /// something turned it into a refusal.
    /// </summary>
    [Fact]
    public async Task ALostRaceBecomesAConflictRatherThanAThrow()
    {
        await using var pipeline = new SamplePipeline();

        var result = await pipeline.Sender.Send(new SampleCommand("Ada", SampleOutcome.LoseTheRace));

        Assert.True(result.IsFailure);
        Assert.Equal(ConcurrencyErrors.StaleVersionCode, result.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, result.Error.Category);

        // Caught above the transaction, which had already rolled back on the way out: the refusal
        // is reported and nothing half-written survives it.
        Assert.Equal(
            [PipelineJournal.Begun, PipelineJournal.Handled, PipelineJournal.RolledBack],
            pipeline.Journal.Entries);
    }

    /// <summary>
    /// The other shape of the same collision: two callers creating the row a unique index guards.
    /// Neither read a version to be stale about, so the index is what refuses the second — and
    /// until it was translated, the most ordinary way for two dispatchers to plan one job answered
    /// 500.
    /// </summary>
    [Fact]
    public async Task ADuplicateWriteBecomesAConflictRatherThanAThrow()
    {
        await using var pipeline = new SamplePipeline();

        var result = await pipeline.Sender.Send(new SampleCommand("Ada", SampleOutcome.WriteADuplicate));

        Assert.True(result.IsFailure);
        Assert.Equal(ConcurrencyErrors.DuplicateCode, result.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, result.Error.Category);

        Assert.Equal(
            [PipelineJournal.Begun, PipelineJournal.Handled, PipelineJournal.RolledBack],
            pipeline.Journal.Entries);
    }

    /// <summary>
    /// A command whose database dropped underneath it runs again, from the beginning, and commits
    /// once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of the retry, and the reason the transaction boundary belongs to the unit of work:
    /// a failover or a reset connection took the transaction with it, so there is nothing to
    /// resume — the whole operation begins again. Before this, every such blip was a 500 to a phone
    /// or a board.
    /// </para>
    /// <para>
    /// The journal is what makes it meaningful: two transactions begun, one commit, and the handler
    /// having run inside the second — a retry that committed twice, or committed the attempt that
    /// failed, would read differently here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ATransientDatabaseFailureRunsTheWholeCommandAgain()
    {
        await using var pipeline = new SamplePipeline(transientFailures: 1);

        var result = await pipeline.Sender.Send(new SampleCommand("Ada", SampleOutcome.Succeed));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                PipelineJournal.Begun,
                PipelineJournal.RetriedAfterFailure,
                PipelineJournal.RolledBack,
                PipelineJournal.Begun,
                PipelineJournal.Handled,
                PipelineJournal.Saved,
                PipelineJournal.Committed,
            ],
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
