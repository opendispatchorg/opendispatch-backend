using System.Text.Json;
using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync.PushOps;

/// <summary>
/// Shape rules for a pushed batch.
/// </summary>
/// <remarks>
/// <para>
/// The split between what is refused here and what comes back as a conflict is deliberate, and it
/// is the difference between an operation the server <em>cannot answer</em> and one it answers
/// with "no". A refusal here fails the whole batch, so only things that make the batch itself
/// unanswerable belong: without an id there is no way to report on an operation, and without a
/// payload there is nothing to apply. An unknown operation type, by contrast, is answered one
/// operation at a time — a device deployed ahead of its server would otherwise hold a queue that
/// can never be emptied.
/// </para>
/// <para>
/// It does not look for duplicate ids. Two operations sharing an id are one operation, which is
/// what idempotency means; the handler answers once and neither is lost.
/// </para>
/// </remarks>
internal sealed class PushOpsValidator : AbstractValidator<PushOpsCommand>
{
    public PushOpsValidator()
    {
        RuleFor(command => command.TechnicianId)
            .NotEqual(default(TechnicianId)).WithMessage("A push must say whose work it is.");

        RuleFor(command => command.Ops)
            .NotNull().WithMessage("A push must carry a batch, even an empty one.")
            .Must(ops => ops.Count <= PushOpsCommand.MaxOps)
            .WithMessage($"A push carries at most {PushOpsCommand.MaxOps} operations.");

        RuleForEach(command => command.Ops).ChildRules(op =>
        {
            op.RuleFor(pushed => pushed.Id)
                .NotEqual(default(SyncOpId))
                .WithMessage("An operation must carry the id its device gave it.");

            op.RuleFor(pushed => pushed.BaseVersion)
                .GreaterThanOrEqualTo(0)
                .WithMessage("An operation cannot be based on a version that never existed.");

            // A JsonElement that was never given a value cannot even be written back out, so an
            // operation without one is refused here rather than blowing up when the log tries to
            // record it — which is what step 21 said the edge would do with it.
            op.RuleFor(pushed => pushed.Payload)
                .Must(payload => payload.ValueKind is not JsonValueKind.Undefined)
                .WithMessage("An operation must carry a payload, even an empty object.");
        });
    }
}
