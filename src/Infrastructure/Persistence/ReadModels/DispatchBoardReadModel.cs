using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.ReadModels;

/// <inheritdoc cref="IDispatchBoardReadModel"/>
/// <remarks>
/// <para>
/// Three queries and no aggregates. The board wants a customer's name and a site's address against
/// every stop, and neither lives on <c>Job</c> — it holds a <c>CustomerId</c> and a
/// <c>ServiceLocationId</c>, because aggregates reference each other by id. Through the
/// repositories that would be a customer load per job; here it is a join, which is the whole reason
/// Document 2 §5 gives read-heavy paths a projection port instead.
/// </para>
/// <para>
/// The joins are explicit for the same reason: there is no navigation property to follow, and there
/// should not be. A relationship in the model is a way to reach through one aggregate into another,
/// and the price of not having one is three <c>join</c> clauses in the one place that needs them.
/// </para>
/// <para>
/// Every query is scoped by the tenant filter without saying so, because each of them starts from a
/// <c>DbSet</c> and the filter is applied to the model rather than to the call site (step 29). A
/// board that leaked another organization's day would be the worst possible place for it.
/// </para>
/// <para>
/// Scalars are selected in SQL and the value objects are rebuilt in memory afterwards. A
/// <c>TimeWindow</c> is a complex type and a <c>GeoPoint</c> a converted one; asking the provider to
/// materialise either inside a projection is asking for a translation failure at runtime for no
/// gain, since the columns come back either way.
/// </para>
/// </remarks>
internal sealed class DispatchBoardReadModel(AppDbContext context) : IDispatchBoardReadModel
{
    public async Task<DispatchBoard> GetAsync(TimeWindow day, CancellationToken ct)
    {
        var crew = await context.Technicians
            // By name, because a lane is read by a person. The id breaks ties so two technicians
            // with one name always draw in the same order.
            .OrderBy(technician => technician.Name)
            .ThenBy(technician => technician.Id)
            .Select(technician => new
            {
                technician.Id,
                technician.Name,
                ShiftStart = technician.Shift.Start,
                ShiftEnd = technician.Shift.End,
                technician.HomeBase,
            })
            .ToListAsync(ct);

        var stops = await (
            from assignment in context.Assignments
            where assignment.ScheduledStart >= day.Start && assignment.ScheduledStart < day.End
            join job in context.Jobs on assignment.JobId equals job.Id
            join customer in context.Customers on job.CustomerId equals customer.Id
            orderby assignment.ScheduledStart, assignment.Id
            select new
            {
                assignment.Id,
                assignment.TechnicianId,
                assignment.Sequence,
                assignment.ScheduledStart,
                assignment.TravelMin,
                Job = new
                {
                    job.Id,
                    job.Status,
                    job.Priority,
                    job.RequiredSkill,
                    WindowStart = job.Window.Start,
                    WindowEnd = job.Window.End,
                    job.EstimatedDuration,
                    job.Location,
                    CustomerName = customer.Name,
                    Address = customer.Locations
                        .Where(location => location.Id == job.LocationId)
                        .Select(location => location.Address)
                        .FirstOrDefault(),
                },
            }).ToListAsync(ct);

        // Work promised inside the day that nobody is going to. "Schedulable" is the domain's own
        // answer, so a job that was cancelled does not sit in the pile forever — and a job the
        // optimiser dropped, which keeps its status and loses its stop, appears here rather than
        // vanishing from the board altogether.
        // Materialised so the provider sees a plain collection to turn into an IN (…) — the set
        // itself belongs to Job, which is where the question "may this still be planned" is
        // answered for the optimiser and the assign path too.
        var waiting = Job.SchedulableStatuses.ToArray();

        var unassigned = await (
            from job in context.Jobs
            where waiting.Contains(job.Status)
            where job.Window.Start < day.End && day.Start < job.Window.End
            where !context.Assignments.Any(assignment => assignment.JobId == job.Id)
            join customer in context.Customers on job.CustomerId equals customer.Id
            orderby job.Window.Start, job.Id
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
                CustomerName = customer.Name,
                Address = customer.Locations
                    .Where(location => location.Id == job.LocationId)
                    .Select(location => location.Address)
                    .FirstOrDefault(),
            }).ToListAsync(ct);

        var lanes = stops
            .GroupBy(stop => stop.TechnicianId)
            .ToDictionary(lane => lane.Key, lane => lane.ToList());

        return new DispatchBoard(
            day,
            [
                .. crew.Select(technician => new BoardRoute(
                    technician.Id,
                    technician.Name,
                    new TimeWindow(technician.ShiftStart, technician.ShiftEnd),
                    technician.HomeBase,
                    lanes.TryGetValue(technician.Id, out var lane)
                        ?
                        [
                            .. lane.Select(stop => new BoardStop(
                                stop.Id,
                                stop.Sequence,
                                stop.ScheduledStart,
                                stop.TravelMin,
                                new BoardJob(
                                    stop.Job.Id,
                                    stop.Job.Status,
                                    stop.Job.Priority,
                                    stop.Job.RequiredSkill,
                                    new TimeWindow(stop.Job.WindowStart, stop.Job.WindowEnd),
                                    stop.Job.EstimatedDuration,
                                    stop.Job.Location,
                                    stop.Job.CustomerName,
                                    stop.Job.Address ?? string.Empty))),
                        ]
                        : [])),
            ],
            [
                .. unassigned.Select(job => new BoardJob(
                    job.Id,
                    job.Status,
                    job.Priority,
                    job.RequiredSkill,
                    new TimeWindow(job.WindowStart, job.WindowEnd),
                    job.EstimatedDuration,
                    job.Location,
                    job.CustomerName,
                    job.Address ?? string.Empty)),
            ]);
    }
}
