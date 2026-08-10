using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Customers;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Jobs.CreateJob;

/// <summary>
/// Resolves the customer's service location, then books the job against it.
/// </summary>
/// <remarks>
/// <para>
/// The first handler in the system to read one aggregate in order to build another, and the read
/// is not a convenience. A <c>Job</c> carries the coordinates of the place the work happens so the
/// scheduler can build a distance matrix without loading a customer per stop — so somebody has to
/// copy that point across, and doing it here means the job's location is by construction one of
/// the customer's locations rather than whatever a caller sent.
/// </para>
/// <para>
/// That check is also the only one there is. A <c>ServiceLocation</c> is owned by its customer, so
/// EF cannot express a foreign key from <c>Job.LocationId</c> to it (verified at step 26) and
/// nothing in the database will refuse a job pointing at a site that does not exist. This is where
/// that reference is made good.
/// </para>
/// <para>
/// Two loads, two failures worth telling apart: no such customer, or a customer without that site.
/// Both are misses rather than conflicts — a stale picker, most likely — and neither says whether
/// the row exists under another tenant, because the repository is scoped and genuinely cannot see.
/// </para>
/// </remarks>
internal sealed class CreateJobHandler(
    IJobRepository jobs,
    ICustomerRepository customers,
    ITenantContext tenant)
    : IRequestHandler<CreateJobCommand, Result<JobId>>
{
    public async Task<Result<JobId>> Handle(CreateJobCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers
            .GetAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure<JobId>(CustomerErrors.NotFound(command.CustomerId));
        }

        var location = customer.Locations.FirstOrDefault(site => site.Id == command.LocationId);

        if (location is null)
        {
            return Result.Failure<JobId>(
                CustomerErrors.LocationNotFound(command.CustomerId, command.LocationId));
        }

        var job = Job.Create(
            tenant.OrgId,
            customer.Id,
            location.Id,
            location.Point,
            command.RequiredSkill,
            command.Priority,
            // UTC for the same reason as a technician's shift: the column is timestamptz and
            // Npgsql refuses a non-zero offset, while the offset itself is something nothing in
            // the system reads.
            new TimeWindow(command.WindowStart.ToUniversalTime(), command.WindowEnd.ToUniversalTime()),
            command.EstimatedDuration);

        jobs.Add(job);

        return Result.Success(job.Id);
    }
}
