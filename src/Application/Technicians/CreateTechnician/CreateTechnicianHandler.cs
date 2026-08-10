using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Technicians.CreateTechnician;

/// <summary>
/// Creates the technician and stages them. The pipeline decides whether they are kept.
/// </summary>
/// <remarks>
/// <para>
/// The organization comes from the ambient tenant and never from the command, exactly as in the
/// Customers slice — the query filters scope reads, so a row filed under the wrong organization
/// would vanish from the tenant that created it rather than announce itself.
/// </para>
/// <para>
/// <strong>The shift is converted to UTC here, and this is the first place in the system that has
/// to.</strong> Npgsql refuses a <c>DateTimeOffset</c> with a non-zero offset against
/// <c>timestamptz</c>, so a caller in London in summer sending <c>+01:00</c> would otherwise get an
/// exception from the driver rather than a working shift. Converting loses only the offset, which
/// nothing reads: a <c>DateTimeOffset</c> is an absolute instant, <c>TimeWindow</c> compares by
/// instant, and the column stores an instant. Refusing a non-UTC offset instead would be refusing
/// a well-formed time for the storage layer's convenience.
/// </para>
/// </remarks>
internal sealed class CreateTechnicianHandler(ITechnicianRepository technicians, ITenantContext tenant)
    : IRequestHandler<CreateTechnicianCommand, Result<TechnicianId>>
{
    public Task<Result<TechnicianId>> Handle(
        CreateTechnicianCommand command,
        CancellationToken cancellationToken)
    {
        var technician = Technician.Create(
            tenant.OrgId,
            command.Name,
            command.Skills,
            new TimeWindow(command.ShiftStart.ToUniversalTime(), command.ShiftEnd.ToUniversalTime()),
            new GeoPoint(command.Latitude, command.Longitude));

        technicians.Add(technician);

        return Task.FromResult(Result.Success(technician.Id));
    }
}
