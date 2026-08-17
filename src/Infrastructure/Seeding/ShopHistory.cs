using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>What a run of <see cref="ShopHistory"/> wrote.</summary>
/// <param name="Days">How many trading days it covered.</param>
/// <param name="Customers">Customers on the books, each with one or two sites.</param>
/// <param name="Jobs">Jobs booked across the whole period.</param>
/// <param name="Assignments">Stops planned — one per job that was not cancelled before planning.</param>
/// <param name="Invoices">Bills raised.</param>
/// <param name="Ops">Field operations in the sync log, for the retention window only.</param>
public sealed record ShopHistorySummary(
    int Days,
    int Customers,
    int Jobs,
    int Assignments,
    int Invoices,
    int Ops);

/// <summary>
/// A year behind the counter: what a shop's database looks like after it has been used.
/// </summary>
/// <remarks>
/// <para>
/// The demo dataset is one day, which is right for a demonstration and useless for a measurement —
/// every read path in this system is fast over forty jobs. This writes the shape that actually
/// stresses them: a customer book that grew, a job table with a year of finished work in it, one
/// stop per job on eight technicians' days, the invoices those jobs were billed on, and a sync log
/// as long as the retention window keeps it.
/// </para>
/// <para>
/// <strong>Through the aggregates, not through INSERT.</strong> Every job here walks the same state
/// machine a real one does — scheduled, dispatched, en route, in progress, completed, invoiced,
/// paid — so what the measurements run against is data with the same invariants as production data,
/// including the statuses the schedulable predicate filters on. Rows written by hand would measure
/// a table this system could never have produced.
/// </para>
/// <para>
/// <strong>The events are dropped rather than published.</strong> Twelve thousand jobs walked
/// through seven transitions raise something over eighty thousand domain events, which the outbox
/// would faithfully write and the in-process publisher would faithfully deliver — an hour of
/// seeding to announce a year that already happened. History is not news: the reactions to these
/// transitions belong to the days they occurred on. <see cref="AggregateRoot.ClearDomainEvents"/>
/// before each save is what says so.
/// </para>
/// <para>
/// <strong>Deterministic.</strong> One fixed seed, so two runs produce the same shop and two
/// measurements are comparable. The randomness is in the shape of the work — which technician, how
/// long, which of the handful of trades — not in how much of it there is.
/// </para>
/// </remarks>
public sealed class ShopHistory(AppDbContext database)
{
    /// <summary>How far back the history runs.</summary>
    /// <remarks>
    /// Three hundred trading days: a year of a shop that works five days a week. Weekends are
    /// skipped because a job table with Saturday work in it every week would flatter the sync
    /// queries — real ones cluster, and a cursor that walks a week is walking five days of stops.
    /// </remarks>
    public const int TradingDays = 300;

    /// <summary>Jobs booked on each of those days.</summary>
    /// <remarks>
    /// Forty across eight technicians is five stops each — a full day for a trade that drives
    /// between them, and the number Document 1's shop is described as doing.
    /// </remarks>
    public const int JobsPerDay = 40;

    /// <summary>How many customers the shop has taken on.</summary>
    public const int CustomerCount = 400;

    /// <summary>
    /// How much of the sync log survives, matching what <c>prune --days 30</c> leaves.
    /// </summary>
    /// <remarks>
    /// Seeding three hundred days of op log would measure a table a real deployment never has: the
    /// runbook has pruning on a schedule, so the honest size of that table is one window's worth.
    /// </remarks>
    public const int OpLogDays = 30;

    /// <summary>Entities per save. Large enough to be few round trips, small enough to hold.</summary>
    private const int BatchSize = 2_000;

    /// <summary>The seed, so the shop is the same shop every time.</summary>
    private const int RandomSeed = 20260816;

    private static readonly string[] Trades = ["hvac", "plumbing", "electrical", "refrigeration", "boiler", "gas"];

    private static readonly string[] Streets =
    [
        "SE Ladd Ave", "NE Alberta St", "SW Barbur Blvd", "N Killingsworth St", "SE Division St",
        "NW Thurman St", "SE Woodstock Blvd", "N Lombard St", "SE Belmont St", "NE Fremont St",
    ];

    /// <summary>
    /// Writes the history into an organization that already has its crew.
    /// </summary>
    /// <param name="organization">The tenant to write under.</param>
    /// <param name="today">The day the demo data occupies; history runs backwards from the day before.</param>
    /// <param name="crew">The technicians to plan the work onto, as the seeder created them.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What was written.</returns>
    public async Task<ShopHistorySummary> WriteAsync(
        OrgId organization,
        DateTimeOffset today,
        IReadOnlyList<Technician> crew,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crew);

        if (crew.Count == 0)
        {
            throw new InvalidOperationException("A shop's history needs a crew to have done the work.");
        }

        var random = new Random(RandomSeed);
        var customers = await WriteCustomersAsync(organization, random, ct).ConfigureAwait(false);

        var jobs = 0;
        var assignments = 0;
        var invoices = 0;
        var ops = 0;
        var pending = 0;

        for (var back = 1; back <= TradingDays; back++)
        {
            var day = TradingDayBefore(today, back);
            var logged = back <= OpLogDays;

            for (var placed = 0; placed < JobsPerDay; placed++)
            {
                var site = customers[random.Next(customers.Count)];
                var technician = crew[placed % crew.Count];
                var start = day.AddHours(7).AddMinutes(30 * (placed / crew.Count));
                var duration = TimeSpan.FromMinutes(45 + (15 * random.Next(0, 5)));

                var job = Job.Create(
                    organization,
                    site.Customer,
                    site.Location,
                    site.Point,
                    Trades[random.Next(Trades.Length)],
                    Priority(random),
                    new TimeWindow(start, start.Add(duration).AddHours(2)),
                    duration);

                // One job in twenty is called off before anybody drives anywhere: a cancelled job
                // with no stop is a shape the board and the sync scope both have to handle.
                if (random.Next(20) == 0)
                {
                    job.Cancel();
                    Stage(job);
                    jobs++;
                    pending++;
                    continue;
                }

                job.Schedule();
                job.Dispatch();
                job.MarkEnRoute();
                job.MarkInProgress();

                var finished = start.Add(duration);
                job.MarkCompleted(finished);

                var labour = Math.Round(duration.TotalHours, 2);
                job.RecordLine(LineItemKind.Labor, "Attendance", (decimal)labour, Money.FromDollars(95m), finished);

                if (random.Next(3) != 0)
                {
                    job.RecordLine(LineItemKind.Part, "Parts fitted", 1m, Money.FromDollars(20m + random.Next(0, 240)), finished);
                }

                var stop = Assignment.Create(
                    organization,
                    job.Id,
                    technician.Id,
                    (placed / crew.Count) + 1,
                    start,
                    12d + random.Next(0, 24));

                Stage(job);
                database.Assignments.Add(stop);
                jobs++;
                assignments++;
                pending += 2;

                // Most finished work is billed and paid; a tail of it is still out, which is what
                // makes the invoice table something other than a copy of the job table.
                if (random.Next(10) != 0)
                {
                    var invoice = Invoice.CreateFromJob(organization, job.Id, finished.AddDays(1));

                    foreach (var line in job.Lines)
                    {
                        invoice.AddLineItem(line.Kind, line.Description, line.Quantity, line.UnitPrice);
                    }

                    job.MarkInvoiced();

                    if (random.Next(4) != 0)
                    {
                        invoice.MarkPaid();
                        job.MarkPaid();
                    }

                    invoice.ClearDomainEvents();
                    database.Invoices.Add(invoice);
                    invoices++;
                    pending++;
                }

                if (logged)
                {
                    foreach (var op in OpsFor(organization, technician.Id, job, finished))
                    {
                        database.SyncOps.Add(op);
                        ops++;
                        pending++;
                    }
                }
            }

            if (pending >= BatchSize)
            {
                await FlushAsync(ct).ConfigureAwait(false);
                pending = 0;
            }
        }

        await FlushAsync(ct).ConfigureAwait(false);

        return new ShopHistorySummary(TradingDays, customers.Count, jobs, assignments, invoices, ops);
    }

    /// <summary>The customer book, written first because every job points into it.</summary>
    private async Task<List<Site>> WriteCustomersAsync(OrgId organization, Random random, CancellationToken ct)
    {
        var sites = new List<Site>(CustomerCount * 2);

        for (var index = 0; index < CustomerCount; index++)
        {
            var customer = Customer.Create(
                organization,
                $"Customer {index + 1:D4}",
                new ContactInfo($"customer{index + 1:D4}@example.test", $"+1-503-555-{index % 10000:D4}"));

            // Most people have one address; some businesses have two sites, which is what makes the
            // location lookup a real lookup rather than a one-to-one.
            var count = random.Next(4) == 0 ? 2 : 1;

            for (var site = 0; site < count; site++)
            {
                // Scattered over roughly the metropolitan area the demo crew works, so the
                // scheduler's distances are the distances a real day has.
                var point = new GeoPoint(
                    45.42d + (random.NextDouble() * 0.22d),
                    -122.82d + (random.NextDouble() * 0.28d));

                var location = customer.AddLocation(
                    site == 0 ? "Main" : "Second site",
                    $"{random.Next(100, 9999)} {Streets[random.Next(Streets.Length)]}, Portland",
                    point);

                sites.Add(new Site(customer.Id, location, point));
            }

            database.Customers.Add(customer);
        }

        await FlushAsync(ct).ConfigureAwait(false);

        return sites;
    }

    /// <summary>
    /// The field operations behind one visit, as a phone would have pushed them.
    /// </summary>
    /// <remarks>
    /// Three per job — on the way, on site, done — which is what the op log actually fills up with.
    /// The payloads are the real shapes, because a pull that reads them has to deserialize them.
    /// </remarks>
    private static IEnumerable<SyncOpRecord> OpsFor(
        OrgId organization,
        TechnicianId technician,
        Job job,
        DateTimeOffset finished)
    {
        var statuses = new[] { "EnRoute", "InProgress", "Completed" };

        for (var index = 0; index < statuses.Length; index++)
        {
            var at = finished.AddMinutes(-30 + (15 * index));

            yield return SyncOpRecord.Applied(
                SyncOpId.From(Guid.NewGuid()),
                organization,
                technician,
                "job",
                job.Id.Value,
                "status_change",
                $$"""{"status":"{{statuses[index]}}"}""",
                job.Version,
                at,
                at.AddSeconds(30));
        }
    }

    /// <summary>
    /// Stages a job with its events dropped — see this class's remarks on why history is not news.
    /// </summary>
    private void Stage(Job job)
    {
        job.ClearDomainEvents();
        database.Jobs.Add(job);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        await database.SaveChangesAsync(ct).ConfigureAwait(false);

        // Without this the tracker keeps every entity of the whole run and each save re-examines
        // all of them: the last day would take minutes and the process would hold a year of jobs
        // in memory for no reason.
        database.ChangeTracker.Clear();
    }

    /// <summary>
    /// <paramref name="back"/> trading days before <paramref name="today"/>, skipping weekends.
    /// </summary>
    private static DateTimeOffset TradingDayBefore(DateTimeOffset today, int back)
    {
        var day = today;

        for (var counted = 0; counted < back; counted++)
        {
            do
            {
                day = day.AddDays(-1);
            }
            while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        }

        return day;
    }

    /// <summary>A day's work is mostly ordinary, with the urgent tail a shop actually has.</summary>
    private static JobPriority Priority(Random random) => random.Next(10) switch
    {
        0 => JobPriority.Emergency,
        1 or 2 => JobPriority.High,
        9 => JobPriority.Low,
        _ => JobPriority.Normal,
    };

    /// <summary>One place a job can be booked at, and the customer who owns it.</summary>
    private readonly record struct Site(CustomerId Customer, ServiceLocationId Location, GeoPoint Point);
}
