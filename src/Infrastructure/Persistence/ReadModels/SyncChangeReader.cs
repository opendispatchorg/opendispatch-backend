using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.ReadModels;

/// <inheritdoc cref="ISyncChangeReader"/>
/// <remarks>
/// <para>
/// Four queries, and each one answers a question a device would otherwise have to ask by
/// downloading its whole day: which of my stops moved, which of my jobs changed, which stops left
/// me, and which were deleted outright.
/// </para>
/// <para>
/// <strong>Everything is windowed by <c>change_seq</c></strong>, the stamp step 41 puts on every row
/// from inside the writing transaction. The comparison is <c>&gt;=</c> rather than <c>&gt;</c>
/// because the cursor is the oldest transaction that could still have been in flight when it was
/// issued: a row stamped exactly at it may not have been visible then, so it is sent again rather
/// than assumed delivered.
/// </para>
/// <para>
/// <strong>A job is in scope because a stop for it is this technician's.</strong> That definition
/// needs no per-device state on the server, which is what lets a device sync from a cursor and
/// nothing else. Its consequence is deliberate: a job whose stop moved away leaves the scope with
/// the stop, and is reported by the removal rather than by a second change saying the job is no
/// longer relevant.
/// </para>
/// <para>
/// A stop is sent whenever <em>either</em> it or its job changed, and so is the job. A newly planned
/// stop is the case that forces it: the assignment is new but the job may not have been touched for
/// weeks, and sending the stop without the work it is for would leave a device holding a visit to
/// nowhere.
/// </para>
/// <para>
/// The stamp is read with <c>EF.Property</c> inline in every predicate rather than through a helper,
/// because a helper is a method call the provider cannot see into: the query stops translating and
/// falls back to loading the table. It reads worse and it has to.
/// </para>
/// <para>
/// Scalars are selected in SQL and the value objects rebuilt afterwards, for the same reason as the
/// dispatch board: a <c>TimeWindow</c> is a complex type and a <c>GeoPoint</c> a converted one, and
/// asking the provider to materialise either inside a projection buys a translation failure for
/// nothing.
/// </para>
/// </remarks>
internal sealed class SyncChangeReader(AppDbContext context) : ISyncChangeReader
{
    public async Task<SyncScopePage> ReadAsync(
        TechnicianId technician,
        SyncCursor since,
        int maxTransactions,
        int maxRows,
        CancellationToken ct)
    {
        var mine = context.Assignments.Where(assignment => assignment.TechnicianId == technician);

        // Where this page stops, decided before anything is read: the stamp of the last whole
        // transaction that fits. Null means everything waiting fits, which is the ordinary case and
        // the only one where the caller's watermark is the right answer.
        var ceiling = await CeilingAsync(technician, mine, since, maxTransactions, maxRows, ct)
            .ConfigureAwait(false);
        var upTo = ceiling ?? long.MaxValue;

        // A stop is news if it moved, or if the work it is for changed underneath it — the second
        // is what carries a re-windowed job to a device whose plan is untouched.
        var stops = await (
            from assignment in mine
            join job in context.Jobs on assignment.JobId equals job.Id
            where (EF.Property<long>(assignment, ChangeStamps.PropertyName) >= since.Value
                    && EF.Property<long>(assignment, ChangeStamps.PropertyName) <= upTo)
                || (EF.Property<long>(job, ChangeStamps.PropertyName) >= since.Value
                    && EF.Property<long>(job, ChangeStamps.PropertyName) <= upTo)
            orderby assignment.ScheduledStart, assignment.Id
            select new
            {
                assignment.Id,
                assignment.JobId,
                assignment.Sequence,
                assignment.ScheduledStart,
                assignment.TravelMin,
                assignment.Version,
            }).ToListAsync(ct);

        // No-tracking, and not merely as an optimisation: the projection carries the job's recorded
        // lines, which are an owned collection, and EF refuses to track one without its owner in the
        // result. A read model has nothing to track for anyway — nobody saves a projection.
        var jobs = await (
            from assignment in mine.AsNoTracking()
            join job in context.Jobs.AsNoTracking() on assignment.JobId equals job.Id
            join customer in context.Customers.AsNoTracking() on job.CustomerId equals customer.Id
            where (EF.Property<long>(assignment, ChangeStamps.PropertyName) >= since.Value
                    && EF.Property<long>(assignment, ChangeStamps.PropertyName) <= upTo)
                || (EF.Property<long>(job, ChangeStamps.PropertyName) >= since.Value
                    && EF.Property<long>(job, ChangeStamps.PropertyName) <= upTo)
            orderby job.Id
            select new
            {
                job.Id,
                job.Status,
                job.Priority,
                job.RequiredSkill,
                WindowStart = job.Window.Start,
                WindowEnd = job.Window.End,
                job.EstimatedDuration,
                job.Location,
                job.Notes,
                job.NotesRecordedAt,
                job.Lines,
                job.Version,
                CustomerName = customer.Name,
                Address = customer.Locations
                    .Where(location => location.Id == job.LocationId)
                    .Select(location => location.Address)
                    .FirstOrDefault(),
            }).ToListAsync(ct);

        // Handed to somebody else: the row is still there, so its own stamp reports it. Read across
        // the tenant rather than within this technician's day, because the whole point is that it
        // is no longer in it.
        var handedOn = await context.Assignments
            .Where(assignment => assignment.TechnicianId != technician)
            .Where(assignment => EF.Property<long>(assignment, ChangeStamps.PropertyName) >= since.Value
                && EF.Property<long>(assignment, ChangeStamps.PropertyName) <= upTo)
            .Select(assignment => assignment.Id)
            .ToListAsync(ct);

        // Deleted outright: nothing left to carry a stamp, so the note written when it went is what
        // answers. Scoped to this technician, because a removal is only news to whoever was going.
        var deleted = await context.SyncRemovals
            .Where(removal => removal.TechnicianId == technician)
            .Where(removal => removal.Entity == SyncRemoval.StopEntity)
            .Where(removal => EF.Property<long>(removal, ChangeStamps.PropertyName) >= since.Value
                && EF.Property<long>(removal, ChangeStamps.PropertyName) <= upTo)
            .Select(removal => removal.EntityId)
            .ToListAsync(ct);

        var changes = new SyncScopeChanges(
            [
                .. jobs.Select(job => new SyncJobState(
                    job.Id,
                    job.Status,
                    job.Priority,
                    job.RequiredSkill,
                    new TimeWindow(job.WindowStart, job.WindowEnd),
                    job.EstimatedDuration,
                    job.Location,
                    job.CustomerName,
                    job.Address ?? string.Empty,
                    job.Notes,
                    job.NotesRecordedAt,
                    [
                        .. job.Lines.Select(line => new SyncJobLine(
                            line.Id,
                            line.Kind,
                            line.Description,
                            line.Quantity,
                            line.UnitPrice)),
                    ],
                    job.Version)),
            ],
            [
                .. stops.Select(stop => new SyncStopState(
                    stop.Id,
                    stop.JobId,
                    stop.Sequence,
                    stop.ScheduledStart,
                    stop.TravelMin,
                    stop.Version)),
            ],
            [.. handedOn, .. deleted.Select(AssignmentId.From)]);

        return new SyncScopePage(changes, ceiling);
    }

    /// <summary>
    /// The stamp this page stops at, or <see langword="null"/> if everything waiting fits in one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four cheap questions rather than one expensive one: each source is asked for its next
    /// <paramref name="maxTransactions"/> + 1 distinct stamps at or after the cursor, which an index
    /// answers without reading rows. Merged, that is the first
    /// <paramref name="maxTransactions"/> + 1 stamps of the technician's whole stream — because the
    /// merged stream's earliest stamps can only come from some source's earliest stamps.
    /// </para>
    /// <para>
    /// If there are no more than <paramref name="maxTransactions"/>, the page is everything and
    /// there is no ceiling. If there are more, the ceiling is the last stamp that fits <em>whole</em>
    /// — and the caller resumes at one past it, which is safe because a transaction that has
    /// committed cannot gain more rows and its id is never reused.
    /// </para>
    /// <para>
    /// A single transaction bigger than the budget is therefore sent whole, overrunning the page.
    /// That is deliberate: the alternative is a device that asks for the same stamp forever, or one
    /// that skips half of what a re-optimisation did to its day.
    /// </para>
    /// </remarks>
    private async Task<long?> CeilingAsync(
        TechnicianId technician,
        IQueryable<Assignment> mine,
        SyncCursor since,
        int maxTransactions,
        int maxRows,
        CancellationToken ct)
    {
        var wanted = maxTransactions + 1;

        var stamps = new List<(long Stamp, int Rows)>(wanted * 4);

        stamps.AddRange(await StampsAsync(mine, since, wanted, ct).ConfigureAwait(false));

        // The jobs behind this technician's stops: their own stamps move when the office edits the
        // work, which is a change to this technician's world even when the plan did not move.
        stamps.AddRange(await StampsAsync(
            from assignment in mine
            join job in context.Jobs on assignment.JobId equals job.Id
            select job,
            since,
            wanted,
            ct).ConfigureAwait(false));

        stamps.AddRange(await StampsAsync(
            context.Assignments.Where(assignment => assignment.TechnicianId != technician),
            since,
            wanted,
            ct).ConfigureAwait(false));

        stamps.AddRange(await StampsAsync(
            context.SyncRemovals
                .Where(removal => removal.TechnicianId == technician)
                .Where(removal => removal.Entity == SyncRemoval.StopEntity),
            since,
            wanted,
            ct).ConfigureAwait(false));

        // Summed across the sources, because a stamp's real weight on the page is every row every
        // source wrote under it — the same optimise that moved this technician's stops also touched
        // the jobs behind them.
        var weights = new Dictionary<long, int>(stamps.Count);

        foreach (var (stamp, rows) in stamps)
        {
            weights[stamp] = weights.GetValueOrDefault(stamp) + rows;
        }

        var ordered = weights.Keys.Order().Take(wanted).ToList();

        return Ceiling(ordered, weights, maxTransactions, maxRows);
    }

    /// <summary>
    /// Walks the stamps in order and says where the page stops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two budgets, and the transaction is the atom either way: a page ends <em>between</em> stamps
    /// or not at all. It stops at whichever budget runs out first — the transaction count, or the
    /// row count once a stamp would take it past the budget.
    /// </para>
    /// <para>
    /// <strong>The first transaction is always taken, however big it is.</strong> A stamp larger
    /// than the whole row budget would otherwise be a wall: the device asks, receives nothing,
    /// stores the same cursor, and asks again forever.
    /// </para>
    /// <para>
    /// Null means everything waiting fits, which is the ordinary pull and the only case where the
    /// caller's own watermark is the right cursor to hand back.
    /// </para>
    /// </remarks>
    private static long? Ceiling(
        List<long> ordered,
        Dictionary<long, int> weights,
        int maxTransactions,
        int maxRows)
    {
        var rows = 0;

        for (var taken = 0; taken < ordered.Count; taken++)
        {
            var stamp = ordered[taken];

            if (taken > 0 && (taken >= maxTransactions || rows + weights[stamp] > maxRows))
            {
                return ordered[taken - 1];
            }

            rows += weights[stamp];
        }

        // Everything asked for fit — but the caller asked for one more stamp than a page may carry,
        // so a full list still means there is more waiting.
        return ordered.Count > maxTransactions ? ordered[maxTransactions - 1] : null;
    }

    /// <summary>
    /// The next <paramref name="wanted"/> change stamps in a source, from the cursor, with how many
    /// rows each one wrote.
    /// </summary>
    /// <remarks>
    /// A grouped count rather than a distinct list: the stamps alone cannot say whether a page is
    /// forty rows or forty thousand, which is the thing the row budget exists to bound. It is the
    /// same index scan with a count on top.
    /// </remarks>
    private static async Task<List<(long Stamp, int Rows)>> StampsAsync<TEntity>(
        IQueryable<TEntity> source,
        SyncCursor since,
        int wanted,
        CancellationToken ct)
        where TEntity : class
    {
        var counted = await source
            .Select(row => EF.Property<long>(row, ChangeStamps.PropertyName))
            .Where(stamp => stamp >= since.Value)
            .GroupBy(stamp => stamp)
            .OrderBy(group => group.Key)
            .Take(wanted)
            .Select(group => new { Stamp = group.Key, Rows = group.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. counted.Select(row => (row.Stamp, row.Rows))];
    }
}
