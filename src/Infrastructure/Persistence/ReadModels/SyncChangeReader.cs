using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
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
    public async Task<SyncScopeChanges> ReadAsync(
        TechnicianId technician,
        SyncCursor since,
        CancellationToken ct)
    {
        var mine = context.Assignments.Where(assignment => assignment.TechnicianId == technician);

        // A stop is news if it moved, or if the work it is for changed underneath it — the second
        // is what carries a re-windowed job to a device whose plan is untouched.
        var stops = await (
            from assignment in mine
            join job in context.Jobs on assignment.JobId equals job.Id
            where EF.Property<long>(assignment, ChangeStamps.PropertyName) >= since.Value
                || EF.Property<long>(job, ChangeStamps.PropertyName) >= since.Value
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
            where EF.Property<long>(assignment, ChangeStamps.PropertyName) >= since.Value
                || EF.Property<long>(job, ChangeStamps.PropertyName) >= since.Value
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
            .Where(assignment => EF.Property<long>(assignment, ChangeStamps.PropertyName) >= since.Value)
            .Select(assignment => assignment.Id)
            .ToListAsync(ct);

        // Deleted outright: nothing left to carry a stamp, so the note written when it went is what
        // answers. Scoped to this technician, because a removal is only news to whoever was going.
        var deleted = await context.SyncRemovals
            .Where(removal => removal.TechnicianId == technician)
            .Where(removal => removal.Entity == SyncRemoval.StopEntity)
            .Where(removal => EF.Property<long>(removal, ChangeStamps.PropertyName) >= since.Value)
            .Select(removal => removal.EntityId)
            .ToListAsync(ct);

        return new SyncScopeChanges(
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
    }
}
