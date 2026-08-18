using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// The demo shop: one organization, its crew, its customers, and a day's work.
/// </summary>
/// <remarks>
/// <para>
/// A single Portland-area HVAC/plumbing/electrical business, sized so the day is a real
/// scheduling problem rather than a demonstration that arithmetic works. Forty jobs against
/// roughly sixty-four technician-hours is about ninety per cent utilisation once driving is
/// paid for, which is what a busy shop's morning actually looks like — the optimiser has to
/// work, a handful of jobs may genuinely not fit, and the unassigned pile on the board means
/// something.
/// </para>
/// <para>
/// The coordinates are real neighbourhoods across the metro (St. Johns to Gresham is about
/// thirty kilometres), so travel time is a real term in the objective instead of noise. The
/// street addresses and the customer names are invented, and the e-mail domains are
/// <c>.example</c> (RFC 2606) so nothing here can reach anybody.
/// </para>
/// <para>
/// Skills are deliberately scarce in the right places: two technicians hold refrigeration, two
/// hold boiler, and one of those two works a half day. That is what makes a hard constraint bite
/// somewhere, which is the difference between a demo of a scheduler and a demo of a for-loop.
/// </para>
/// <para>
/// Nothing here is scheduled. Every job is booked demand, which is what a dispatcher sees before
/// the day starts and what makes <c>POST /schedule/optimize</c> the demo's opening move. Seeding
/// a plan as well would mean hand-building assignments the engine is meant to produce.
/// </para>
/// </remarks>
internal static class DemoData
{
    /// <summary>
    /// The demo shop's name, which is also its identity: a later run finds the organization it
    /// wrote by this name rather than by an id nothing could have remembered.
    /// </summary>
    public const string OrganizationName = "Ridgeline Mechanical";

    /// <summary>
    /// The crew: eight technicians, varied skills, varied shifts.
    /// </summary>
    /// <remarks>
    /// One skill is spelled "HVAC" against the jobs' "hvac" on purpose. Skills are free text typed
    /// on two screens by two people, matching ignores case throughout
    /// (<c>Technician.Skills</c>, <c>TechPlan.HasSkill</c>), and a dataset that only ever used one
    /// casing would never exercise that.
    /// </remarks>
    public static IReadOnlyList<DemoTechnician> Technicians { get; } =
    [
        new("Marisol Vega", ["hvac", "refrigeration"], Shift(6.5, 15), 45.5780, -122.6870),
        new("Dev Ramanathan", ["HVAC", "electrical"], Shift(7, 15.5), 45.5152, -122.6784),
        new("Alice Nakamura", ["plumbing", "gas"], Shift(7, 16), 45.4690, -122.6570),
        new("Tobias Okonkwo", ["hvac", "boiler"], Shift(8, 16.5), 45.4870, -122.8030),
        new("Priya Deshmukh", ["electrical"], Shift(8, 17), 45.5330, -122.6220),
        new("Grant Whitfield", ["plumbing", "hvac"], Shift(9, 17.5), 45.4210, -122.6700),
        new("Femi Adeyemi", ["refrigeration", "hvac"], Shift(10, 18.5), 45.5050, -122.5730),

        // A half day. Somebody has to be the technician a job does not fit on.
        new("Callum Byrne", ["plumbing", "boiler", "gas"], Shift(8, 12), 45.5880, -122.7560),
    ];

    /// <summary>
    /// The book: twenty-four customers, twenty-eight service locations, forty jobs.
    /// </summary>
    public static IReadOnlyList<DemoCustomer> Customers { get; } =
    [
        new("Rose City Property Group", "ops@rosecityproperty.example", "+1 503 555 0142",
        [
            new("Alberta Court", "4218 NE Alberta St", 45.5590, -122.6460,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Morning, 90),
                new("electrical", JobPriority.Low, DemoWindows.Afternoon, 60),
            ]),
            new("Irvington Apartments", "1725 NE 15th Ave", 45.5450, -122.6580,
            [
                new("plumbing", JobPriority.High, DemoWindows.Morning, 60),
            ]),
            new("Kenton Row", "8302 N Denver Ave", 45.5780, -122.6870,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Afternoon, 75),
            ]),
        ]),

        new("Bridgetown Coffee Roasters", "hello@bridgetownroasters.example", "+1 503 555 0118",
        [
            new("Pearl Roastery", "1122 NW Glisan St", 45.5270, -122.6810,
            [
                new("refrigeration", JobPriority.Emergency, DemoWindows.Early, 90),
            ]),
            new("Hawthorne Cafe", "3702 SE Hawthorne Blvd", 45.5120, -122.6250,
            [
                new("plumbing", JobPriority.Normal, DemoWindows.Morning, 45),
                new("hvac", JobPriority.Low, DemoWindows.Afternoon, 60),
            ]),
        ]),

        new("Cascade Grocery Co-op", "facilities@cascadecoop.example", "+1 503 555 0170",
        [
            new("Woodstock Store", "4525 SE Woodstock Blvd", 45.4780, -122.6100,
            [
                new("refrigeration", JobPriority.High, DemoWindows.Morning, 120),
            ]),
            new("Hillsdale Store", "6425 SW Capitol Hwy", 45.4800, -122.6960,
            [
                new("hvac", JobPriority.Normal, DemoWindows.AnyTime, 90),
            ]),
        ]),

        new("Sandy Boulevard Dental", "front.desk@sandyblvddental.example", "+1 503 555 0163",
        [
            new("Surgery", "4108 NE Sandy Blvd", 45.5330, -122.6220,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Morning, 60),
            ]),
        ]),

        new("Ankeny Street Lofts", "board@ankenylofts.example", null,
        [
            new("Boiler Room", "3312 SE Ankeny St", 45.5250, -122.6260,
            [
                new("boiler", JobPriority.High, DemoWindows.Morning, 120),
            ]),
        ]),

        new("Morrison Building Services", "dispatch@morrisonbuilding.example", "+1 503 555 0129",
        [
            new("Morrison Tower", "820 SE Morrison St", 45.5170, -122.6520,
            [
                new("electrical", JobPriority.Normal, DemoWindows.Morning, 75),
                new("hvac", JobPriority.Normal, DemoWindows.Afternoon, 90),
            ]),
        ]),

        new("Division Street Brewing", "taproom@divisionbrewing.example", "+1 503 555 0155",
        [
            new("Taproom", "4130 SE Division St", 45.5050, -122.6180,
            [
                new("refrigeration", JobPriority.Normal, DemoWindows.Afternoon, 90),
                new("plumbing", JobPriority.Low, DemoWindows.AnyTime, 60),
            ]),
        ]),

        new("Bybee Family Practice", "clinic@bybeefamily.example", "+1 503 555 0107",
        [
            new("Clinic", "1610 SE Bybee Blvd", 45.4690, -122.6570,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Morning, 60),
            ]),
        ]),

        new("Milwaukie Auto Group", "service@milwaukieauto.example", "+1 503 555 0191",
        [
            new("Service Bays", "10725 SE Main St", 45.4460, -122.6390,
            [
                new("electrical", JobPriority.Normal, DemoWindows.Afternoon, 90),
            ]),
        ]),

        new("Foster Road Church", "office@fosterroadchurch.example", null,
        [
            new("Hall", "9210 SE Foster Rd", 45.4700, -122.5700,
            [
                new("boiler", JobPriority.Normal, DemoWindows.Morning, 120),
            ]),
        ]),

        new("Stark Street Elementary", "facilities@starkstreetschool.example", "+1 503 555 0134",
        [
            new("Main Building", "7815 SE Stark St", 45.5170, -122.5730,
            [
                new("hvac", JobPriority.High, DemoWindows.Morning, 75),
                new("plumbing", JobPriority.Normal, DemoWindows.Afternoon, 45),
            ]),
        ]),

        new("Prescott Logistics", "depot@prescottlogistics.example", "+1 503 555 0188",
        [
            new("Depot", "10520 NE Prescott St", 45.5540, -122.5450,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Afternoon, 60),
            ]),
        ]),

        new("Lombard Laundromat", "owner@lombardlaundry.example", "+1 503 555 0146",
        [
            new("Shop Floor", "7412 N Lombard St", 45.5880, -122.7560,
            [
                new("plumbing", JobPriority.High, DemoWindows.Morning, 90),
                new("gas", JobPriority.Normal, DemoWindows.Afternoon, 60),
            ]),
        ]),

        new("Willamette Boathouse", "bookings@willametteboathouse.example", null,
        [
            new("Boathouse", "5305 N Willamette Blvd", 45.5770, -122.7270,
            [
                new("plumbing", JobPriority.Low, DemoWindows.Afternoon, 45),
            ]),
        ]),

        new("Everett House Hotel", "engineering@everetthouse.example", "+1 503 555 0112",
        [
            new("Guest Wing", "2145 NW Everett St", 45.5320, -122.6980,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Morning, 90),
                new("plumbing", JobPriority.Normal, DemoWindows.Late, 60),
            ]),
        ]),

        new("Alder Tower Offices", "building@aldertower.example", "+1 503 555 0176",
        [
            new("Floors 8-12", "808 SW Alder St", 45.5152, -122.6784,
            [
                new("hvac", JobPriority.Emergency, DemoWindows.Midday, 120),
                new("electrical", JobPriority.Normal, DemoWindows.Morning, 60),
            ]),
        ]),

        new("Bond Avenue Clinic", "estates@bondavenueclinic.example", "+1 503 555 0125",
        [
            new("Day Unit", "3550 SW Bond Ave", 45.4990, -122.6710,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Afternoon, 75),
                new("plumbing", JobPriority.Low, DemoWindows.Afternoon, 45),
            ]),
        ]),

        new("Multnomah Village Bakery", "bakery@multnomahvillage.example", "+1 503 555 0198",
        [
            new("Bakehouse", "7825 SW 35th Ave", 45.4640, -122.7120,
            [
                new("gas", JobPriority.High, DemoWindows.Morning, 60),
            ]),
        ]),

        new("Scholls Ferry Veterinary", "reception@schollsferryvet.example", "+1 503 555 0139",
        [
            new("Surgery", "4915 SW Scholls Ferry Rd", 45.4840, -122.7520,
            [
                new("hvac", JobPriority.Normal, DemoWindows.Morning, 60),
                new("electrical", JobPriority.Low, DemoWindows.Afternoon, 45),
            ]),
        ]),

        new("Broadway Fitness Beaverton", "manager@broadwayfitness.example", "+1 503 555 0184",
        [
            new("Gym", "12520 SW Broadway St", 45.4870, -122.8030,
            [
                new("plumbing", JobPriority.Normal, DemoWindows.Morning, 75),
                new("hvac", JobPriority.Normal, DemoWindows.Afternoon, 90),
            ]),
        ]),

        new("Pacific Highway Motors", "workshop@pacifichighwaymotors.example", "+1 503 555 0151",
        [
            new("Workshop", "11250 SW Pacific Hwy", 45.4310, -122.7710,
            [
                new("electrical", JobPriority.Normal, DemoWindows.Morning, 60),
            ]),
        ]),

        new("State Street Wine Bar", "cellar@statestreetwine.example", null,
        [
            new("Cellar", "310 N State St", 45.4210, -122.6700,
            [
                new("refrigeration", JobPriority.Normal, DemoWindows.Afternoon, 60),
            ]),
        ]),

        new("Cornell Road Medical Park", "estates@cornellroadmedical.example", "+1 503 555 0167",
        [
            new("Block C", "12275 NW Cornell Rd", 45.5160, -122.8030,
            [
                new("hvac", JobPriority.High, DemoWindows.Morning, 90),
                new("boiler", JobPriority.Normal, DemoWindows.Afternoon, 90),
            ]),
        ]),

        new("Gresham Cold Storage", "plant@greshamcoldstorage.example", "+1 503 555 0173",
        [
            // The far edge of the metro, thirty kilometres from the depot. Somebody has to drive
            // out here, and the objective is what decides who.
            new("Plant", "480 NE Division St", 45.5010, -122.4300,
            [
                new("plumbing", JobPriority.Normal, DemoWindows.AnyTime, 60),
            ]),
        ]),
    ];

    /// <summary>
    /// The three logins the demo runs on — see <see cref="DemoLogin"/> for why three.
    /// </summary>
    public static IReadOnlyList<DemoLogin> Logins { get; } =
    [
        new("admin", "demo", UserRole.Admin, null),
        new("dispatcher", "demo", UserRole.Dispatcher, null),

        // The one login that has to name somebody real: /sync scopes a request to the technician
        // behind the token, so this has to resolve to a seeded Technician or the field half of the
        // demo answers 401.
        new("marisol", "demo", UserRole.Technician, "Marisol Vega"),
    ];

    private static DemoWindow Shift(double startHour, double endHour) =>
        new(TimeSpan.FromHours(startHour), TimeSpan.FromHours(endHour));
}
