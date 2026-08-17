using System.Net;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Attachments.UploadAttachment;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Customers;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.EraseCustomer;
using OpenDispatch.Application.Customers.UpdateCustomer;
using OpenDispatch.Application.Export.GetExport;
using OpenDispatch.Application.Invoicing.GenerateInvoice;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Attachments;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Customers;

/// <summary>
/// Somebody asks to be forgotten.
/// </summary>
/// <remarks>
/// <para>
/// The acceptance test for the whole duty, and it is deliberately one long flow rather than a case
/// per assertion: erasure is only correct if it is correct <em>everywhere at once</em>, and the way
/// it fails in real systems is that one place is missed — a coordinate on the job, a photograph on
/// a disk, a copy in the export. So this books real work for a real customer, takes a photograph of
/// their house, bills them, and then checks every one of those places.
/// </para>
/// <para>
/// It needs the database and the disk. The claim is about rows and files, and the only thing that
/// can settle it is rows and files.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ErasureFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private const string Name = "Dana Whitlock";
    private const string Email = "dana@whitlock.example";
    private const string Phone = "+44 20 7946 0000";
    private const string Address = "12 Rillington Place, London";
    private const string Note = "Key under the mat, dog in the kitchen.";

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;
    private readonly ApiFactory _factory;

    public ErasureFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    /// <summary>
    /// The whole duty, with photographs on a disk — what a shop hosting this on its own machine
    /// runs.
    /// </summary>
    [Fact]
    public Task ErasesThePersonAndKeepsTheBusiness() =>
        ErasesEverywhere(
            AttachmentStorageSettings.OnDisk(_postgres.AttachmentRoot),
            key => Task.FromResult(File.Exists(Path.Combine(_postgres.AttachmentRoot, key))));

    /// <summary>
    /// The same duty with photographs in a bucket, which is where a deployment on a container
    /// platform keeps them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a duplicate of the test above. Erasure is the one thing in this system that destroys
    /// bytes, and it now has two stores to destroy them in: a delete that reached a disk says
    /// nothing about whether it reached a bucket, and "the photograph is still recoverable after we
    /// told them it was gone" is not a failure anybody would notice from the outside. So the claim
    /// is made where it will actually run.
    /// </para>
    /// <para>
    /// The object store is started inside this test rather than for the class, so the three cases
    /// beside it — which are about rows, not bytes — do not pay for a container.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ErasesThePersonAndKeepsTheBusinessWithContentInABucket()
    {
        await using var minio = await MinioFixture.StartAsync();

        await ErasesEverywhere(minio.Settings, minio.ExistsAsync);
    }

    private async Task ErasesEverywhere(
        AttachmentStorageSettings storage,
        Func<string, Task<bool>> storedContent)
    {
        await using var services = TestHost.Over(_postgres, attachments: storage)
            .BuildServiceProvider(validateScopes: true);

        var customer = await Send(services, new CreateCustomerCommand(Name, Email, Phone));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value, "Home", Address, 51.5074d, -0.1278d));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(3),
            TimeSpan.FromHours(1)));

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], MondayMorning.AddHours(-1), MondayMorning.AddHours(8), 51.5074d, -0.1278d));

        await Send(services, new AssignJobCommand(job.Value, technician.Value, MondayMorning));

        // Through the visit, so the job holds what a real one holds by the time it is billed.
        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress })
        {
            await Send(services, new ChangeJobStatusCommand(job.Value, status));
        }

        await NoteAsync(job.Value);

        var photograph = AttachmentId.From(Guid.NewGuid());
        var stored = await Send(services, new UploadAttachmentCommand(
            photograph,
            job.Value,
            AttachmentKind.Photo,
            "image/jpeg",
            ByteLength: 4,
            new MemoryStream(Encoding.UTF8.GetBytes("JPEG"))));

        Assert.True(
            await storedContent(stored.Value.ServerId),
            $"the arrange step did not store {stored.Value.ServerId}");

        await Send(services, new ChangeJobStatusCommand(job.Value, JobStatus.Completed));

        var invoice = await Send(services, new GenerateInvoiceCommand(job.Value,
        [
            new InvoiceLine(LineItemKind.Labor, "Two hours", 2m, 65m),
            new InvoiceLine(LineItemKind.Part, "Thermostat", 1m, 120m),
        ]));

        var billed = invoice.Value.Total;
        Assert.Equal(Money.FromDollars(250m), billed);

        // The act itself.
        var erased = await Send(services, new EraseCustomerCommand(customer.Value));
        Assert.True(erased.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);

        // Nothing left in the database that says who they were.
        var them = await context.Customers.SingleAsync(row => row.Id == customer.Value);
        Assert.Equal(Tombstone.Text, them.Name);
        Assert.Null(them.Contact.Email);
        Assert.Null(them.Contact.Phone);
        Assert.NotNull(them.ErasedAt);

        var site = Assert.Single(them.Locations);
        Assert.Equal(Tombstone.Text, site.Address);
        Assert.Equal(Tombstone.Point, site.Point);

        var theirJob = await context.Jobs.SingleAsync(row => row.Id == job.Value);
        Assert.Null(theirJob.Notes);
        Assert.Equal(Tombstone.Point, theirJob.Location);
        Assert.NotNull(theirJob.ErasedAt);

        // The photograph is gone from the database and from wherever its bytes were — the half a
        // database-only erasure leaves behind, and the half nobody notices.
        Assert.Empty(await context.Attachments.Where(row => row.JobId == job.Value).ToListAsync());
        Assert.False(
            await storedContent(stored.Value.ServerId),
            $"{stored.Value.ServerId} is still in the store");

        // And the shop's books are untouched: the work still happened and still cost what it cost.
        var bill = await context.Invoices.SingleAsync(row => row.Id == invoice.Value.Id);
        Assert.Equal(billed, bill.Total);
        Assert.Equal(2, bill.Lines.Count);
        Assert.Equal(JobStatus.Invoiced, theirJob.Status);
        Assert.Equal(MondayMorning, theirJob.Window.Start);
    }

    /// <summary>
    /// The export is a second copy of everything, so it is a second place an erasure has to have
    /// reached — and the one a subject access request is answered from.
    /// </summary>
    [Fact]
    public async Task NothingPersonalSurvivesIntoTheExport()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

        var customer = await Send(services, new CreateCustomerCommand(Name, Email, Phone));
        await Send(services, new AddServiceLocationCommand(customer.Value, "Home", Address, 51.5074d, -0.1278d));
        await Send(services, new EraseCustomerCommand(customer.Value));

        using var scope = services.ActingAs(_tenant);
        var export = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetExportQuery());

        var exported = await export.Value.Customers.SingleAsync(row => row.Id == customer.Value);

        Assert.Equal(Tombstone.Text, exported.Name);
        Assert.Null(exported.Email);
        Assert.Null(exported.Phone);
        Assert.Equal(Tombstone.Text, Assert.Single(exported.Locations).Address);

        // The erasure travels with the record: a reader can tell an answered request from a row
        // somebody never filled in.
        Assert.NotNull(exported.ErasedAt);
    }

    /// <summary>
    /// Erasing is destructive and cannot be undone, so it is the one route on the customers surface
    /// a dispatcher cannot reach — checked over HTTP, because that is where the policy lives.
    /// </summary>
    [Fact]
    public async Task OnlyAnAdminMayErase()
    {
        var org = OrgId.New();
        var dispatcher = $"dispatch-{Guid.NewGuid():N}@whitlock.example";
        var admin = $"admin-{Guid.NewGuid():N}@whitlock.example";

        await _factory.SeedUserAsync(org, dispatcher, "whitlock-heating", UserRole.Dispatcher);
        await _factory.SeedUserAsync(org, admin, "whitlock-heating", UserRole.Admin);

        using var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        using var refused = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/customers/{id}/erase")
                .Authorized(await client.LoginAsync(dispatcher, "whitlock-heating")));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The admin gets as far as the handler, which is what says the policy is the only thing in
        // the way: a customer nobody has is a 404 rather than a 403.
        using var allowed = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/customers/{id}/erase")
                .Authorized(await client.LoginAsync(admin, "whitlock-heating")));

        Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
    }

    /// <summary>
    /// What stops an erasure being undone by the next person who opens the customer's record: the
    /// office may no longer write to it, and neither may a phone that still holds their job.
    /// </summary>
    [Fact]
    public async Task NothingCanBeWrittenAboutThemAfterwards()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

        var customer = await Send(services, new CreateCustomerCommand(Name, Email, Phone));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value, "Home", Address, 51.5074d, -0.1278d));

        var job = await Send(services, new CreateJobCommand(
            customer.Value, location.Value, "hvac", JobPriority.Normal,
            MondayMorning, MondayMorning.AddHours(3), TimeSpan.FromHours(1)));

        await Send(services, new EraseCustomerCommand(customer.Value));

        var renamed = await Send(services, new UpdateCustomerCommand(customer.Value, Name, Email, Phone));
        Assert.Equal(CustomerErrors.Erased(customer.Value), renamed.Error);

        var booked = await Send(services, new CreateJobCommand(
            customer.Value, location.Value, "hvac", JobPriority.Normal,
            MondayMorning, MondayMorning.AddHours(3), TimeSpan.FromHours(1)));
        Assert.Equal(CustomerErrors.Erased(customer.Value), booked.Error);

        // A photograph from a phone that was out of signal when the erasure ran.
        var late = await Send(services, new UploadAttachmentCommand(
            AttachmentId.From(Guid.NewGuid()),
            job.Value,
            AttachmentKind.Photo,
            "image/jpeg",
            ByteLength: 4,
            new MemoryStream(Encoding.UTF8.GetBytes("JPEG"))));

        Assert.Equal(JobErrors.Erased(job.Value), late.Error);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Equal(Tombstone.Text, (await context.Customers.SingleAsync(row => row.Id == customer.Value)).Name);
    }

    /// <summary>
    /// Writes the technician's note directly, because the only way in through the application is a
    /// sync push from the technician who owns the stop — machinery this test is not about.
    /// </summary>
    private async Task NoteAsync(JobId job)
    {
        await using var context = _postgres.NewContext(_tenant);

        var writing = await context.Jobs.SingleAsync(row => row.Id == job);
        writing.RecordNotes(Note, MondayMorning.AddHours(1));

        await context.SaveChangesAsync();
    }

    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
