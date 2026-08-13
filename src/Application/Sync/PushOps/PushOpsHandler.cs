using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Observability;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Sync.PushOps;

/// <summary>
/// Replays a device's queue against the domain, once each, and reports what became of every
/// operation in it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A refused operation does not fail the push.</strong> This is the decision the whole
/// slice is shaped around, and it runs against the pipeline's usual rule that a failed result
/// rolls the transaction back: a batch that applied four operations and refused the fifth must
/// keep the four, or a technician's morning is lost to one stale tap. So conflicts are carried
/// inside a <em>successful</em> result. A failure here means the push itself could not happen.
/// </para>
/// <para>
/// <strong>Idempotency has two halves.</strong> The op log answers for previous pushes, in one
/// query for the whole batch; a set answers for this one, so an id written down twice by a
/// confused device is one operation and gets one answer. Neither half guesses: an operation is
/// applied when its id has never been recorded, and the primary key on the log is what holds the
/// line if two pushes race past the query at once.
/// </para>
/// <para>
/// <strong>Legality is asked of the domain, not restated here.</strong> A status operation goes
/// through the same <see cref="JobIntents.Drivable"/> table and the same
/// <c>Job.CanTransition</c> question the REST endpoint uses, and reports the same
/// <c>JobErrors.IllegalTransition</c> when the answer is no — a phone and a dispatcher asking the
/// same question get the same answer.
/// </para>
/// <para>
/// Ops are applied in the order the device recorded them, which is what lets a whole morning
/// arrive at once: en route, on site, finished are three operations that are only legal in that
/// sequence, and the job they are applied to is the same tracked instance each time.
/// </para>
/// </remarks>
internal sealed class PushOpsHandler(
    ISyncOpStore log,
    ISyncCursorSource cursors,
    IJobRepository jobs,
    ITenantContext tenant,
    IClock clock,
    SyncMetrics metrics)
    : IRequestHandler<PushOpsCommand, Result<PushedBatch>>
{
    public async Task<Result<PushedBatch>> Handle(PushOpsCommand command, CancellationToken cancellationToken)
    {
        var alreadyApplied = await log
            .FindAppliedAsync([.. command.Ops.Select(op => op.Id)], cancellationToken)
            .ConfigureAwait(false);

        var answered = new HashSet<SyncOpId>();
        var applied = new List<SyncOpId>();
        var conflicts = new List<SyncOpConflict>();

        foreach (var op in command.Ops)
        {
            // One answer per id. A device that wrote the same operation down twice performed it
            // once, and putting it in both lists would tell it two things about one action.
            if (!answered.Add(op.Id))
            {
                continue;
            }

            // Applied by an earlier push. Reported as applied because that is what it is from the
            // device's point of view; anything else leaves it queueing the operation forever.
            if (alreadyApplied.Contains(op.Id))
            {
                applied.Add(op.Id);
                continue;
            }

            var outcome = await ApplyAsync(op, cancellationToken).ConfigureAwait(false);

            if (outcome.IsFailure)
            {
                // Not written to the log: the log records work that happened. A refusal that was
                // recorded would be answered from memory next time rather than re-judged against
                // a job that may since have moved.
                conflicts.Add(new SyncOpConflict(op.Id, outcome.Error!));

                // The one refusal in this system that never reaches a log level or a status code
                // — it rides inside a 200 — so this counter is the only place a client shipping
                // operations this server cannot apply becomes visible (Document 3, step 54).
                metrics.Conflicted(outcome.Error!.Code);
                continue;
            }

            log.Add(Recorded(op, command.TechnicianId));
            applied.Add(op.Id);
        }

        // Counted once for the batch rather than per operation: the number that matters is how
        // much field work a push carried, and Add(n) is one measurement where n increments are
        // n. Ops an earlier push had already applied are included, because they are what this
        // device believes it did — undercounting them would make a phone stuck in a retry loop
        // look idle.
        metrics.Applied(applied.Count);

        // Taken before the batch commits, so it is at or below this transaction's own changes and
        // a pull with it returns them. That is deliberate: a device with a conflict is told what
        // was refused, and finds out what is true by pulling.
        var cursor = await cursors.CurrentAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new PushedBatch(applied, conflicts, cursor));
    }

    private async Task<Result> ApplyAsync(PushedOp op, CancellationToken cancellationToken)
    {
        // Checked before anything is loaded: an operation this server cannot apply should not
        // cost a query, and "job" is the only thing the field workflow speaks about today.
        if (!string.Equals(op.Entity, FieldOps.JobEntity, StringComparison.Ordinal)
            || op.Type is not (FieldOps.StatusChange or FieldOps.AddNote or FieldOps.AddLineItem))
        {
            return Result.Failure(SyncErrors.UnsupportedOperation(op.Entity, op.Type));
        }

        var id = JobId.From(op.EntityId);
        var job = await jobs.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure(JobErrors.NotFound(id));
        }

        var observedAt = Observed(op.ClientTs);

        return op.Type switch
        {
            FieldOps.StatusChange => ChangeStatus(job, op, observedAt),
            FieldOps.AddNote => RecordNotes(job, op, observedAt),
            _ => RecordLine(job, op, observedAt),
        };
    }

    /// <summary>
    /// When an operation happened, as far as this server will believe: what the device said, or now,
    /// whichever is earlier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A device may be late. It may not be from the future.</strong> Free text is
    /// last-write-wins by the writer's own clock (Document 2 §10), which is right — a technician who
    /// wrote a note in a basement at nine wrote it at nine, whatever time it reaches the server. But
    /// the same rule hands a phone whose clock is a day fast every conflict it will ever have, until
    /// that date passes, and nothing anywhere reports it. The symptom is the office's note quietly
    /// losing to a stale one.
    /// </para>
    /// <para>
    /// Clamping is the smallest answer that keeps the honest half: a late device still wins nothing
    /// it should not, and a fast one is reduced to "now", which is the worst it could have been
    /// telling the truth about. It is deliberately not a rejection — an operation is not wrong
    /// because the phone that recorded it has the wrong date, and refusing it would lose real work.
    /// </para>
    /// <para>
    /// What the device actually said is still recorded, raw, on the op-log row. A clamped log would
    /// hide the broken clock rather than being the one place it can be found.
    /// </para>
    /// </remarks>
    private DateTimeOffset Observed(DateTimeOffset claimed)
    {
        var now = clock.UtcNow;

        return claimed < now ? claimed : now;
    }

    private Result ChangeStatus(Job job, PushedOp op, DateTimeOffset observedAt)
    {
        var read = OpPayloads.ReadStatusChange(op.Payload);

        if (!read.Succeeded)
        {
            return Result.Failure(SyncErrors.MalformedOperation(op.Type, read.Problem!));
        }

        var wanted = read.Value!;

        // Narrower than the state machine, and the gap is the same one the REST endpoint refuses:
        // Invoiced and Paid are legal moves that only invoicing makes, and Unscheduled is where a
        // job starts. A phone asking for one of those is asking for something no actor does, which
        // is a malformed operation rather than a conflict about the state of the world.
        if (!JobIntents.Drivable.TryGetValue(wanted.Status, out var drive))
        {
            return Result.Failure(
                SyncErrors.MalformedOperation(op.Type, $"a job is never moved to {wanted.Status} from the field."));
        }

        if (!job.CanTransition(wanted.Status))
        {
            return Result.Failure(JobErrors.IllegalTransition(job.Status, wanted.Status));
        }

        // The device's own clock, not the server's: work finished in a basement at two and
        // reported at six finished at two. Falling back to when it says the operation happened
        // rather than to now, for the same reason — and clamped either way, because a completion
        // dated tomorrow is the same broken clock arriving by a different field.
        drive(job, wanted.CompletedAt is { } finished ? Observed(finished) : observedAt);

        return Result.Success();
    }

    private static Result RecordNotes(Job job, PushedOp op, DateTimeOffset observedAt)
    {
        var read = OpPayloads.ReadNote(op.Payload);

        if (!read.Succeeded)
        {
            return Result.Failure(SyncErrors.MalformedOperation(op.Type, read.Problem!));
        }

        // Last-write-wins, asked of the domain rather than decided here, and reported before it is
        // attempted so that losing carries its own code — a superseded note is not a malformed
        // one, and a device should be able to tell the difference without reading English.
        if (!job.CanRecordNotes(observedAt))
        {
            return Result.Failure(SyncErrors.NotesSuperseded(job.NotesRecordedAt!.Value));
        }

        return Refusable(op, () => job.RecordNotes(read.Value!, observedAt));
    }

    private static Result RecordLine(Job job, PushedOp op, DateTimeOffset observedAt)
    {
        var read = OpPayloads.ReadLine(op.Payload);

        if (!read.Succeeded)
        {
            return Result.Failure(SyncErrors.MalformedOperation(op.Type, read.Problem!));
        }

        var line = read.Value!;

        return Refusable(
            op,
            () => job.RecordLine(line.Kind, line.Description, line.Quantity, line.UnitPrice, observedAt));
    }

    /// <summary>
    /// Runs a domain call whose input came off the wire, reporting a refusal as a conflict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place this slice catches a <see cref="DomainException"/>, and the exception to the
    /// rule the status path follows. A state machine has a question that can be asked without
    /// throwing, so that path asks it. "Is this a well-formed line" does not: the guards live on
    /// <c>JobLine</c>, and a copy of them here would be the second opinion the domain exists to
    /// prevent — one that drifts the day somebody adds a rule to only one of them.
    /// </para>
    /// <para>
    /// It reports what the domain said rather than reinterpreting it, so nothing is
    /// misattributed. And it is narrow on purpose: one call, wrapped, because the input is a JSON
    /// object a phone produced and one bad quantity must not take the whole batch down with it.
    /// </para>
    /// </remarks>
    private static Result Refusable(PushedOp op, Action apply)
    {
        try
        {
            apply();

            return Result.Success();
        }
        catch (DomainException refused)
        {
            return Result.Failure(SyncErrors.MalformedOperation(op.Type, refused.Message));
        }
    }

    /// <remarks>
    /// The <em>raw</em> <c>ClientTs</c> goes in, not the clamped instant the domain was given. The
    /// log is what the device said; clamping it here would hide the one piece of evidence that a
    /// phone's clock is wrong, which is the only place anybody could ever find out.
    /// </remarks>
    private SyncOpRecord Recorded(PushedOp op, TechnicianId technician) =>
        SyncOpRecord.Applied(
            op.Id,
            tenant.OrgId,
            technician,
            op.Entity,
            op.EntityId,
            op.Type,
            op.Payload.GetRawText(),
            op.BaseVersion,
            op.ClientTs,
            clock.UtcNow);
}
