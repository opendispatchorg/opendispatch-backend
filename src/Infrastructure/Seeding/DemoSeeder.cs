using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// Loads <see cref="DemoData"/> into a database — <c>make seed</c>, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only resolvable in Development.</strong> <see cref="DemoSeeding.AddDemoSeeding"/>
/// registers this type nowhere else, so a production host cannot reach it even if something asked
/// — the guard is the absence of a registration rather than a flag this class checks, because a
/// flag is something a later caller can pass wrong.
/// </para>
/// <para>
/// It writes through the <c>DbContext</c> rather than through the feature slices, which is the one
/// place in the system that does. Two reasons, and both are about the seeder being a tool rather
/// than a caller: there is no command that creates an <c>Organization</c> (nothing in the plan
/// creates a tenant over HTTP), and re-running has to be able to clear what the last run wrote,
/// which is not something any handler offers. The aggregates are still built through their own
/// factories, so every invariant a booked job has when a dispatcher books one, it has here.
/// </para>
/// <para>
/// <strong>Every read ignores the tenant filter, and states the organization itself.</strong> The
/// seeder runs outside a request, so nothing has resolved a tenant for it and a filtered query
/// would throw before it could find the organization it is about to become. Reading with
/// <c>IgnoreQueryFilters</c> and an explicit <c>OrgId</c> is what step 29 anticipated a seeder
/// would need.
/// </para>
/// </remarks>
public sealed class DemoSeeder(
    AppDbContext database,
    IUserStore users,
    IPasswordHasher passwords,
    IClock clock)
{
    /// <summary>
    /// Loads the demo shop's crew, customers and a day's work, replacing whatever a previous run
    /// left behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Re-runnable on purpose: a demo gets driven into a state somebody wants to undo, and the
    /// answer to that has to be one command rather than dropping the database. The organization is
    /// found by name and kept — so its id survives across runs and a token issued before one still
    /// names a tenant that exists — and everything under it is deleted and written again.
    /// </para>
    /// <para>
    /// The delete is scoped to this organization on every table. That is the second guard behind
    /// the registration one: even pointed at a database it should not be, this seeder can only
    /// touch rows belonging to a tenant called <see cref="DemoData.OrganizationName"/>.
    /// </para>
    /// </remarks>
    /// <returns>What was loaded, for the caller to report.</returns>
    public async Task<DemoSeed> SeedAsync(CancellationToken ct)
    {
        var organization = await ExistingOrganizationAsync(ct).ConfigureAwait(false);

        if (organization is null)
        {
            organization = Organization.Create(DemoData.OrganizationName);
            database.Organizations.Add(organization);
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await ClearAsync(organization.Id, ct).ConfigureAwait(false);
        }

        var day = DayOf(clock.UtcNow);

        foreach (var technician in DemoData.Technicians)
        {
            database.Technicians.Add(Technician.Create(
                organization.Id,
                technician.Name,
                technician.Skills,
                technician.Shift.On(day),
                new GeoPoint(technician.Lat, technician.Lng)));
        }

        var locations = 0;
        var jobs = 0;

        foreach (var definition in DemoData.Customers)
        {
            var customer = Customer.Create(
                organization.Id,
                definition.Name,
                new ContactInfo(definition.Email, definition.Phone));

            foreach (var site in definition.Locations)
            {
                var point = new GeoPoint(site.Lat, site.Lng);
                var locationId = customer.AddLocation(site.Label, site.Address, point);
                locations++;

                foreach (var work in site.Jobs)
                {
                    // Booked demand and nothing more: a seeded job is Unscheduled, exactly as one
                    // created over HTTP is, and the plan is what the optimiser is for.
                    database.Jobs.Add(Job.Create(
                        organization.Id,
                        customer.Id,
                        locationId,
                        point,
                        work.Skill,
                        work.Priority,
                        work.Window.On(day),
                        TimeSpan.FromMinutes(work.Minutes)));

                    jobs++;
                }
            }

            database.Customers.Add(customer);
        }

        await database.SaveChangesAsync(ct).ConfigureAwait(false);

        return new DemoSeed(
            organization.Id,
            organization.Name,
            new TimeWindow(day, day.AddDays(1)),
            DemoData.Technicians.Count,
            DemoData.Customers.Count,
            locations,
            jobs,
            DemoData.Logins);
    }

    /// <summary>
    /// Puts the demo logins into the user store, resolving the technician one to a real seeded
    /// technician.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="SeedAsync"/> because it has to happen somewhere else. The user
    /// store is an in-memory singleton (step 44), so a login written by the <c>make seed</c>
    /// process would die with it — the host that serves the demo is the only process that can
    /// usefully hold one, and it calls this on startup. <see cref="SeedAsync"/> reports the same
    /// credentials so a developer knows what to type; this is what makes them work.
    /// </para>
    /// <para>
    /// Does nothing if the demo data has not been seeded: there is no organization to sign into
    /// and no technician for the field login to be.
    /// </para>
    /// </remarks>
    /// <returns>How many logins were registered — zero if nothing has been seeded yet.</returns>
    public async Task<int> RegisterLoginsAsync(CancellationToken ct)
    {
        var organization = await ExistingOrganizationAsync(ct).ConfigureAwait(false);

        if (organization is null)
        {
            return 0;
        }

        var crew = await database.Technicians
            .IgnoreQueryFilters()
            .Where(technician => technician.OrgId == organization.Id)
            .Select(technician => new { technician.Name, technician.Id })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byName = crew.ToDictionary(
            technician => technician.Name,
            technician => technician.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var login in DemoData.Logins)
        {
            // A technician login with no matching technician is left without one rather than
            // refused: AuthUser does not validate itself, and the sync endpoints report the gap as
            // a 401 that says what is missing. Renaming a technician in DemoData without renaming
            // it here is the only way to get there.
            var technician = login.TechnicianName is { } name && byName.TryGetValue(name, out var id)
                ? id
                : (TechnicianId?)null;

            await users.AddAsync(
                new AuthUser(
                    UserId.New(),
                    organization.Id,
                    login.Username,
                    passwords.Hash(login.Password),
                    login.Role,
                    technician),
                ct)
                .ConfigureAwait(false);
        }

        return DemoData.Logins.Count;
    }

    /// <summary>Midnight UTC of the day an instant falls in.</summary>
    /// <remarks>
    /// The demo is always today's, so it is worth running before every showing rather than being
    /// a fixed date that ages into last year. UTC because that is the only day the system has —
    /// see <see cref="DemoWindows"/>.
    /// </remarks>
    private static DateTimeOffset DayOf(DateTimeOffset instant) =>
        new(instant.UtcDateTime.Date, TimeSpan.Zero);

    private Task<Organization?> ExistingOrganizationAsync(CancellationToken ct) =>
        database.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Name == DemoData.OrganizationName, ct);

    /// <summary>
    /// Deletes everything the demo organization owns, so a second run leaves one day's work rather
    /// than two.
    /// </summary>
    /// <remarks>
    /// Set-based deletes rather than loading and removing: the change tracker has nothing to
    /// contribute to "delete this tenant's rows", and the owned children (service locations,
    /// invoice lines, job lines) go with their owners through the cascades the initial migration
    /// declares. Attachment <em>content</em> is left on disk — the rows that named it are gone, and
    /// a development storage directory is not worth a second failure mode here.
    /// </remarks>
    private async Task ClearAsync(OrgId organization, CancellationToken ct)
    {
        await database.Assignments.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.SyncRemovals.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.SyncOps.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.Attachments.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.Invoices.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.Jobs.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.Customers.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await database.Technicians.IgnoreQueryFilters()
            .Where(row => row.OrgId == organization).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
