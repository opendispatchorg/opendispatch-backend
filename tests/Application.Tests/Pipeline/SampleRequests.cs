using FluentValidation;
using MediatR;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>How the sample handler should end.</summary>
internal enum SampleOutcome
{
    /// <summary>Return a successful result carrying the name it was given.</summary>
    Succeed,

    /// <summary>Return a failure — an expected refusal, not a bug.</summary>
    Fail,

    /// <summary>Throw, standing in for a bug or a database that is not there.</summary>
    Throw,
}

/// <summary>
/// A command that ends however the test tells it to.
/// </summary>
/// <remarks>
/// The sample the step asks for. It lives in the test project rather than in <c>Application</c>
/// because it is a test fixture: shipping a do-nothing command in the application layer would be
/// the sort of temporary code the build plan asks to avoid, and the first real slice (step 32)
/// exercises the same pipeline for real.
/// </remarks>
internal sealed record SampleCommand(string Name, SampleOutcome Outcome) : ICommand<string>;

/// <summary>Rejects a nameless command, so validation has something to refuse.</summary>
internal sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
{
    public SampleCommandValidator() =>
        RuleFor(command => command.Name).NotEmpty().WithMessage("A name is required.");
}

/// <summary>Records that it ran, then ends as instructed.</summary>
internal sealed class SampleCommandHandler(PipelineJournal journal)
    : IRequestHandler<SampleCommand, Result<string>>
{
    /// <summary>The failure the handler reports when told to fail.</summary>
    public static Error Refused { get; } = Error.Conflict("sample.refused", "The sample handler refused.");

    public Task<Result<string>> Handle(SampleCommand command, CancellationToken cancellationToken)
    {
        journal.Record(PipelineJournal.Handled);

        return Task.FromResult(command.Outcome switch
        {
            SampleOutcome.Succeed => Result.Success(command.Name),
            SampleOutcome.Fail => Result.Failure<string>(Refused),
            _ => throw new InvalidOperationException("The sample handler was told to throw."),
        });
    }
}

/// <summary>A read, so the command-only half of the transaction rule has something to prove.</summary>
internal sealed record SampleQuery(string Name) : IQuery<string>;

/// <summary>Records that it ran and answers.</summary>
internal sealed class SampleQueryHandler(PipelineJournal journal)
    : IRequestHandler<SampleQuery, Result<string>>
{
    public Task<Result<string>> Handle(SampleQuery query, CancellationToken cancellationToken)
    {
        journal.Record(PipelineJournal.Handled);

        return Task.FromResult(Result.Success(query.Name));
    }
}
