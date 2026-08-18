using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// What a seeding run loaded, so the caller can report it without counting rows itself.
/// </summary>
/// <param name="Organization">The tenant everything was written under.</param>
/// <param name="OrganizationName">Its name, which is also how a later run finds it again.</param>
/// <param name="Day">The UTC calendar day the work was booked for — what to pass to the board.</param>
/// <param name="Technicians">How many of the crew were loaded.</param>
/// <param name="Customers">How many customers were loaded.</param>
/// <param name="Locations">How many service locations were loaded across them.</param>
/// <param name="Jobs">How much work was booked, all of it unscheduled.</param>
/// <param name="Logins">
/// The credentials the day can be driven with. Reported rather than written here — see
/// <see cref="DemoSeeder.RegisterLoginsAsync"/> for why the host establishes them, not the seeder.
/// </param>
/// <param name="History">
/// The year of finished business behind the day, or <see langword="null"/> at demo scale — see
/// <see cref="DemoScale"/>.
/// </param>
public sealed record DemoSeed(
    OrgId Organization,
    string OrganizationName,
    TimeWindow Day,
    int Technicians,
    int Customers,
    int Locations,
    int Jobs,
    IReadOnlyList<DemoLogin> Logins,
    ShopHistorySummary? History = null);
