using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// Every aggregate through a real PostGIS database and back, including the collections its root
/// owns.
/// </summary>
/// <remarks>
/// These are the tests the conversions in step 25 were proved by a throwaway entity for, now that
/// there are real ones. Between them the aggregates exercise all eight typed ids, all three
/// stored value objects and the concurrency stamp, so nothing is asserted twice for its own sake.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AggregatePersistenceTests
{
    // Its own organization per test. The query filters then make that into isolation for free,
    // which is why nothing here has to care what else the shared container is holding.
    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public AggregatePersistenceTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task RoundTripsAJob()
    {
        var window = new TimeWindow(
            new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero));
        var job = JobBuilder.Any().ForOrg(_tenant)
            .WithSkill("Boiler service")
            .WithPriority(JobPriority.Emergency)
            .At(new GeoPoint(51.5080, -0.1281))
            .InWindow(window)
            .Lasting(TimeSpan.FromMinutes(90))
            .Build();
        job.Schedule();

        await SaveAsync(context => context.Jobs.Add(job));

        await using var read = NewContext();
        var loaded = await read.Jobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(job.OrgId, loaded.OrgId);
        Assert.Equal(job.CustomerId, loaded.CustomerId);
        Assert.Equal(job.LocationId, loaded.LocationId);
        Assert.Equal("Boiler service", loaded.RequiredSkill);
        Assert.Equal(JobPriority.Emergency, loaded.Priority);
        Assert.Equal(window, loaded.Window);
        Assert.Equal(TimeSpan.FromMinutes(90), loaded.EstimatedDuration);

        // Reloaded as Scheduled, not as the Unscheduled every job is constructed in — proof the
        // materialisation constructor is the one EF used.
        Assert.Equal(JobStatus.Scheduled, loaded.Status);

        // Separately, not as a GeoPoint: a swapped axis is a legal point in the wrong place.
        Assert.Equal(51.5080, loaded.Location.Lat, precision: 6);
        Assert.Equal(-0.1281, loaded.Location.Lng, precision: 6);
    }

    /// <summary>
    /// What a technician records in the field, which is the one collection in the system stored
    /// as JSON on its owner's row rather than in a table of its own.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the JSON document has to carry the same converted values a
    /// column would — a typed id, <c>Money</c> as cents, an instant in UTC — and there is no
    /// column type to catch it if one arrives as something else.
    /// </remarks>
    [Fact]
    public async Task RoundTripsWhatWasRecordedInTheField()
    {
        var recordedAt = new DateTimeOffset(2026, 8, 10, 14, 30, 0, TimeSpan.FromHours(-5));
        var job = JobBuilder.Any().ForOrg(_tenant).Build();
        job.RecordNotes("Meter behind the boiler; access via side gate.", recordedAt);
        job.RecordLine(LineItemKind.Labor, "Diagnostic", 1.5m, Money.FromDollars(95m), recordedAt);
        job.RecordLine(LineItemKind.Part, "Run capacitor 45/5", 2m, Money.FromDollars(28.50m), recordedAt);

        await SaveAsync(context => context.Jobs.Add(job));

        await using var read = NewContext();
        var loaded = await read.Jobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal("Meter behind the boiler; access via side gate.", loaded.Notes);
        Assert.Equal(recordedAt, loaded.NotesRecordedAt);
        Assert.Equal(2, loaded.Lines.Count);

        var labour = loaded.Lines[0];
        Assert.Equal(job.Lines[0].Id, labour.Id);
        Assert.Equal(LineItemKind.Labor, labour.Kind);
        Assert.Equal("Diagnostic", labour.Description);
        Assert.Equal(1.5m, labour.Quantity);
        Assert.Equal(9_500L, labour.UnitPrice.Cents);
        Assert.Equal(recordedAt, labour.RecordedAt);

        // The order they were recorded in survives, which is the only thing a list of them means.
        Assert.Equal(5_700L, loaded.Lines[1].LineTotal.Cents);
    }

    [Fact]
    public async Task RoundTripsAnAssignment()
    {
        var start = new DateTimeOffset(2026, 8, 10, 9, 15, 0, TimeSpan.Zero);
        var assignment = AssignmentBuilder.Any().ForOrg(_tenant).AtSequence(3).StartingAt(start).AfterTravel(12.5).Build();

        await SaveAsync(context => context.Assignments.Add(assignment));

        await using var read = NewContext();
        var loaded = await read.Assignments.SingleAsync(a => a.Id == assignment.Id);

        Assert.Equal(assignment.OrgId, loaded.OrgId);
        Assert.Equal(assignment.JobId, loaded.JobId);
        Assert.Equal(assignment.TechnicianId, loaded.TechnicianId);
        Assert.Equal(3, loaded.Sequence);
        Assert.Equal(start, loaded.ScheduledStart);
        Assert.Equal(12.5, loaded.TravelMin);
    }

    /// <summary>
    /// The skill set has to come back with its case-insensitive comparer. If it does not, nothing
    /// throws — matching simply becomes case-sensitive, and the symptom is a job no technician can
    /// be assigned to.
    /// </summary>
    [Fact]
    public async Task RoundTripsATechnicianAndKeepsSkillMatchingCaseInsensitive()
    {
        var technician = TechnicianBuilder.Any().ForOrg(_tenant).Named("Ada").Skilled("HVAC", "Gas Safe").Build();

        await SaveAsync(context => context.Technicians.Add(technician));

        await using var read = NewContext();
        var loaded = await read.Technicians.SingleAsync(t => t.Id == technician.Id);

        Assert.Equal("Ada", loaded.Name);
        Assert.Equal(technician.Shift, loaded.Shift);
        Assert.Equal(technician.HomeBase.Lat, loaded.HomeBase.Lat, precision: 6);
        Assert.Equal(2, loaded.Skills.Count);

        Assert.True(loaded.HasSkill("hvac"));
        Assert.True(loaded.HasSkill("gas safe"));
    }

    [Fact]
    public async Task SavesServiceLocationsWithTheirCustomerAndRemovesThemWithIt()
    {
        var customer = CustomerBuilder.Any().ForOrg(_tenant).Named("Riverside Ltd").Build();
        var home = customer.AddLocation("Home", "1 Riverside", new GeoPoint(51.5080, -0.1281));
        customer.AddLocation("Depot", "2 Riverside", new GeoPoint(51.5194, -0.1270));

        await SaveAsync(context => context.Customers.Add(customer));

        await using (var reload = NewContext())
        {
            var saved = await reload.Customers.SingleAsync(c => c.Id == customer.Id);

            Assert.Equal(2, saved.Locations.Count);
            var depot = saved.Locations.Single(location => location.Label == "Depot");
            Assert.Equal("2 Riverside", depot.Address);
            Assert.Equal(51.5194, depot.Point.Lat, precision: 6);

            saved.RemoveLocation(home);
            await reload.SaveChangesAsync();
        }

        await using var read = NewContext();
        var afterRemoval = await read.Customers.SingleAsync(c => c.Id == customer.Id);

        Assert.Equal("Depot", Assert.Single(afterRemoval.Locations).Label);
        Assert.Equal(1, await CountRowsAsync("service_locations", "customer_id", customer.Id.Value));
    }

    [Fact]
    public async Task SavesLineItemsWithTheirInvoiceAndDeletesThemWithIt()
    {
        var invoice = InvoiceBuilder.Any().ForOrg(_tenant).Build();
        invoice.AddLineItem(LineItemKind.Labor, "Two hours on site", 2m, Money.FromDollars(90m));
        invoice.AddLineItem(LineItemKind.Part, "Expansion vessel", 1m, Money.FromDollars(64.50m));

        await SaveAsync(context => context.Invoices.Add(invoice));

        await using (var reload = NewContext())
        {
            var saved = await reload.Invoices.SingleAsync(i => i.Id == invoice.Id);

            Assert.Equal(2, saved.Lines.Count);
            Assert.Equal(InvoiceStatus.Draft, saved.Status);

            // Recomputed from the reloaded lines, which is only right if quantity and unit price
            // both survived — the total itself is not a column.
            Assert.Equal(24_450L, saved.Total.Cents);

            reload.Invoices.Remove(saved);
            await reload.SaveChangesAsync();
        }

        Assert.Equal(0, await CountRowsAsync("invoices", "id", invoice.Id.Value));
        Assert.Equal(0, await CountRowsAsync("line_items", "invoice_id", invoice.Id.Value));
    }

    /// <summary>
    /// The one aggregate whose tenant is itself, so it is read as itself: an organization is
    /// scoped by its own id, and the context that saved it belongs to a different tenant.
    /// </summary>
    [Fact]
    public async Task RoundTripsAnOrganization()
    {
        var organization = OpenDispatch.Domain.Organizations.Organization.Create("Riverside Heating");

        await SaveAsync(context => context.Organizations.Add(organization));

        await using var read = _postgres.NewContext(organization.Id);
        var loaded = await read.Organizations.SingleAsync(o => o.Id == organization.Id);

        Assert.Equal("Riverside Heating", loaded.Name);
    }

    /// <summary>
    /// Every stored <see cref="GeoPoint"/> has to be a real geography column, not a pair of
    /// doubles that happens to round-trip: PostGIS distance, containment and the GiST index step
    /// 27 adds all need the type.
    /// </summary>
    [Fact]
    public async Task StoresEveryGeoPointAsAGeographyPointColumn()
    {
        var columns = await GeographyColumnsAsync();

        Assert.Equal(("Point", 4326), columns["jobs.location"]);
        Assert.Equal(("Point", 4326), columns["technicians.home_base"]);
        Assert.Equal(("Point", 4326), columns["service_locations.point"]);
    }

    [Fact]
    public async Task AdvancesTheVersionOnSaveAndRefusesAStaleWrite()
    {
        var job = JobBuilder.Any().ForOrg(_tenant).Build();

        await SaveAsync(context => context.Jobs.Add(job));

        await using var dispatcher = NewContext();
        await using var other = NewContext();
        var mine = await dispatcher.Jobs.SingleAsync(j => j.Id == job.Id);
        var theirs = await other.Jobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(0L, mine.Version);

        mine.Schedule();
        await dispatcher.SaveChangesAsync();

        Assert.Equal(1L, mine.Version);

        theirs.Cancel();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());

        await using var read = NewContext();
        var reloaded = await read.Jobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(JobStatus.Scheduled, reloaded.Status);
        Assert.Equal(1L, reloaded.Version);
    }

    /// <summary>
    /// The gap step 41's decision entry named and step 47 closes: a removed owned child leaves
    /// the root's own entry <c>Unchanged</c> and disappears from the live collection, so the
    /// version has to be found some other way or a customer who only lost a location looks
    /// unmodified to anyone comparing versions — a technician's phone among them.
    /// </summary>
    [Fact]
    public async Task RemovingAnOwnedChildAdvancesTheRootsVersionToo()
    {
        var customer = CustomerBuilder.Any().ForOrg(_tenant).Build();
        var locationId = customer.AddLocation("Head office", "1 High Street, London", new GeoPoint(51.5074, -0.1278));
        customer.ClearDomainEvents();

        await SaveAsync(context => context.Customers.Add(customer));

        await using var context = NewContext();
        var mine = await context.Customers.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(0L, mine.Version);

        mine.RemoveLocation(locationId);
        await context.SaveChangesAsync();

        Assert.Equal(1L, mine.Version);

        await using var read = NewContext();
        var reloaded = await read.Customers.SingleAsync(c => c.Id == customer.Id);
        Assert.Empty(reloaded.Locations);
        Assert.Equal(1L, reloaded.Version);
    }

    /// <summary>
    /// A job is planned once. Neither aggregate can say so, and
    /// <c>IAssignmentRepository.GetByJobAsync</c> (step 19) already promises callers it holds, so
    /// the unique index is the only thing keeping the promise.
    /// </summary>
    [Fact]
    public async Task RefusesASecondAssignmentForTheSameJob()
    {
        var jobId = JobId.New();

        await SaveAsync(context => context.Assignments.Add(AssignmentBuilder.Any().ForJob(jobId).Build()));

        await using var context = NewContext();
        context.Assignments.Add(AssignmentBuilder.Any().ForJob(jobId).Build());

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsType<PostgresException>(failure.InnerException);
    }

    private AppDbContext NewContext() => _postgres.NewContext(_tenant);

    private async Task SaveAsync(Action<AppDbContext> arrange)
    {
        await using var context = NewContext();
        arrange(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Counts rows belonging to one root. Scoped rather than a count of the whole table because
    /// the container is shared for the run — a global count would depend on which other tests
    /// had happened to insert anything.
    /// </summary>
    private async Task<int> CountRowsAsync(string table, string column, Guid value)
    {
        await using var connection = await _postgres.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM \"{table}\" WHERE \"{column}\" = @value;",
            connection);
        command.Parameters.AddWithValue("value", value);

        return (int)(long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Dictionary<string, (string Type, int Srid)>> GeographyColumnsAsync()
    {
        await using var connection = await _postgres.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT f_table_name, f_geography_column, type, srid
            FROM geography_columns
            WHERE f_table_schema = 'public';
            """,
            connection);

        var columns = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns[$"{reader.GetString(0)}.{reader.GetString(1)}"] = (reader.GetString(2), reader.GetInt32(3));
        }

        return columns;
    }
}
