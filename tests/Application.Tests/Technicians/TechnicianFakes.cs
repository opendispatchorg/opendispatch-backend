using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Application.Tests.Technicians;

/// <summary>
/// <see cref="ITechnicianRepository"/> over a <see cref="FakeStore{TAggregate}"/>, scoped to a
/// tenant like the real one.
/// </summary>
/// <remarks>
/// The list is ordered by id, which is what the real repository does and why
/// <c>ListTechniciansHandler</c> sorts its own projection: the port's order belongs to the
/// scheduler, whose plans have to be reproducible under a seed. A fake that handed the crew back
/// alphabetically would make the handler's sort look unnecessary.
/// </remarks>
internal sealed class FakeTechnicianRepository(FakeStore<Technician> store, ITenantContext tenant)
    : ITechnicianRepository
{
    public Task<Technician?> GetAsync(TechnicianId id, CancellationToken ct) =>
        Task.FromResult(store.Owned(tenant.OrgId).FirstOrDefault(technician => technician.Id == id));

    public void Add(Technician technician) => store.Stage(technician);

    public Task<IReadOnlyList<Technician>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Technician>>(
            [.. store.Owned(tenant.OrgId).OrderBy(technician => technician.Id.Value)]);
}
