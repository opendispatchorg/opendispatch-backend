using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Attachments;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Export;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Attachments;

/// <summary>
/// Attachment upload over a real host, a real database and a real disk (Document 3, step 50b):
/// the four scenarios the build text's own "Tests" line names.
/// </summary>
/// <remarks>
/// The storage port and the domain factory are already exhaustively covered — <c>AttachmentStoreTests</c>
/// against Postgres and the local disk directly, <c>AttachmentTests</c> against the aggregate. What
/// only a real host adds is the wire: multipart binding, the idempotent command this step writes,
/// and the tenant boundary a real HTTP caller can actually cross.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AttachmentEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public AttachmentEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task StoresTheBlobAndMetadataAndReturnsAServerId()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedJobAsync(client, adminToken);
        var attachmentId = Guid.NewGuid();
        var photograph = Photograph();

        using var response = await UploadAsync(client, techToken, job.Id, attachmentId, "Photo", photograph);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UploadAttachmentResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.ServerId));

        await using var context = _postgres.NewContext(org);
        var stored = await context.Attachments.SingleAsync(row => row.Id == AttachmentId.From(attachmentId));
        Assert.Equal(JobId.From(job.Id), stored.JobId);
        Assert.Equal(body.ServerId, stored.StorageKey.Value);
    }

    [Fact]
    public async Task ReUploadingTheSameClientIdIsIdempotent()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedJobAsync(client, adminToken);
        var attachmentId = Guid.NewGuid();
        var photograph = Photograph();

        using var first = await UploadAsync(client, techToken, job.Id, attachmentId, "Photo", photograph);
        var firstBody = await first.Content.ReadFromJsonAsync<UploadAttachmentResponse>();

        // The retry: the same client id, the same bytes — what a device does when the response to
        // the first attempt never arrived.
        using var second = await UploadAsync(client, techToken, job.Id, attachmentId, "Photo", photograph);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<UploadAttachmentResponse>();

        Assert.Equal(firstBody!.ServerId, secondBody!.ServerId);

        await using var context = _postgres.NewContext(org);
        Assert.Equal(1, await context.Attachments.CountAsync(row => row.Id == AttachmentId.From(attachmentId)));
    }

    [Fact]
    public async Task ACrossTenantUploadIsRejected()
    {
        var home = OrgId.New();
        var elsewhere = OrgId.New();
        using var client = _factory.CreateClient();

        // The job exists — just not in this technician's tenant.
        var adminToken = await client.LoginAsync(await SeedAdminAsync(elsewhere), "riverside-heating");
        var foreignJob = await ABookedJobAsync(client, adminToken);

        var technicianId = await SeedTechnicianAsync(home);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(home, technicianId), "boiler-service-call");

        using var response = await UploadAsync(
            client, techToken, foreignJob.Id, Guid.NewGuid(), "Photo", Photograph());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheAttachmentAppearsInExport()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedJobAsync(client, adminToken);
        var attachmentId = Guid.NewGuid();

        using var uploaded = await UploadAsync(client, techToken, job.Id, attachmentId, "Signature", Photograph());
        var uploadedBody = await uploaded.Content.ReadFromJsonAsync<UploadAttachmentResponse>();

        using var exported = await SendAsync(client, adminToken, HttpMethod.Get, "/export", body: null);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        var export = await exported.Content.ReadFromJsonAsync<ExportResponse>();

        var entry = Assert.Single(export!.Attachments, candidate => candidate.Id == attachmentId);
        Assert.Equal(job.Id, entry.JobId);
        Assert.Equal(AttachmentKind.Signature, entry.Kind);
        Assert.Equal(uploadedBody!.ServerId, entry.ServerId);
    }

    [Fact]
    public async Task ADispatcherCannotReachTheAttachmentsSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "dispatch@vance.example", "vance-refrigeration", UserRole.Dispatcher);
        var token = await client.LoginAsync("dispatch@vance.example", "vance-refrigeration");

        using var response = await UploadAsync(client, token, Guid.NewGuid(), Guid.NewGuid(), "Photo", Photograph());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The read half, which is what an upload is for: the office lists what came off the van and
    /// fetches it back, byte for byte.
    /// </summary>
    /// <remarks>
    /// Until this step a technician could upload a photograph that nothing in the API could ever
    /// return — the storage port's <c>OpenAsync</c> had no caller outside its own test. This is the
    /// caller.
    /// </remarks>
    [Fact]
    public async Task ListsWhatWasCapturedAgainstAJobAndHandsTheBytesBack()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedJobAsync(client, adminToken);
        var attachmentId = Guid.NewGuid();
        var photograph = Photograph();

        using (var uploaded = await UploadAsync(client, techToken, job.Id, attachmentId, "Photo", photograph))
        {
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        }

        // The office lists it. A dispatcher never uploads and always needs to see.
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/jobs/{job.Id}/attachments").Authorized(adminToken);
        using var listed = await client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        var captures = await listed.Content.ReadFromJsonAsync<AttachmentResponse[]>();
        var capture = Assert.Single(captures!);

        Assert.Equal(attachmentId, capture.Id);
        Assert.Equal(job.Id, capture.JobId);
        Assert.Equal(AttachmentKind.Photo, capture.Kind);
        Assert.Equal("image/jpeg", capture.ContentType);
        Assert.Equal(photograph.Length, capture.ByteLength);

        // The link is the server's, so a client composes no URLs of its own.
        Assert.Equal($"/attachments/{attachmentId}/content", capture.ContentUrl);

        using var contentRequest = new HttpRequestMessage(
            HttpMethod.Get, capture.ContentUrl).Authorized(techToken);
        using var content = await client.SendAsync(contentRequest);

        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/jpeg", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(photograph, await content.Content.ReadAsByteArrayAsync());

        // Served as a download rather than as a page in this API's own origin, and with sniffing
        // turned off: bytes somebody uploaded are not something a browser should be invited to
        // interpret.
        Assert.Equal("attachment", content.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", Assert.Single(content.Headers.GetValues("X-Content-Type-Options")));
    }

    /// <summary>
    /// A file this system does not store is refused before any of it is written — the allow-list
    /// that exists because serving uploaded bytes back is what makes a content type dangerous.
    /// </summary>
    [Fact]
    public async Task RefusesAKindOfFileItDoesNotStore()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedJobAsync(client, adminToken);

        using var response = await UploadAsync(
            client, techToken, job.Id, Guid.NewGuid(), "Photo", Photograph(), contentType: "image/svg+xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var context = _postgres.NewContext(org);
        Assert.Empty(await context.Attachments.ToListAsync());
    }

    /// <summary>
    /// Another organization's capture is not found rather than found and refused — the answer that
    /// says nothing about whether it exists.
    /// </summary>
    [Fact]
    public async Task WillNotHandOverAnotherTenantsCapture()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedJobAsync(client, adminToken);
        var attachmentId = Guid.NewGuid();

        using (var uploaded = await UploadAsync(client, techToken, job.Id, attachmentId, "Photo", Photograph()))
        {
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        }

        var stranger = OrgId.New();
        const string strangerName = "admin@whitlock.example";
        await _factory.SeedUserAsync(stranger, strangerName, "whitlock-heating", UserRole.Admin);
        var strangerToken = await client.LoginAsync(strangerName, "whitlock-heating");

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/attachments/{attachmentId}/content").Authorized(strangerToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Not text: a photograph is bytes, matching AttachmentStoreTests' own reasoning for why this
    // is not a string.
    private static byte[] Photograph() =>
        [.. Encoding.UTF8.GetBytes("PNG\r\n\n"), 0x00, 0xFF, 0x7F, 0x01, .. new byte[512]];

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string token,
        Guid jobId,
        Guid attachmentId,
        string kind,
        byte[] content,
        string contentType = "image/jpeg")
    {
        // The part carries a content type, as a real client's does: it is what the server records
        // and what a download later answers with, and the domain refuses anything outside its
        // allow-list.
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent
        {
            { new StringContent(attachmentId.ToString()), "AttachmentId" },
            { new StringContent(jobId.ToString()), "JobId" },
            { new StringContent(kind), "Kind" },
            { part, "file", "capture.bin" },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/jobs/{jobId}/attachments")
        {
            Content = form,
        }.Authorized(token);

        return client.SendAsync(request);
    }

    private async Task<string> SeedAdminAsync(OrgId org)
    {
        const string username = "admin@riverside.example";
        await _factory.SeedUserAsync(org, username, "riverside-heating", UserRole.Admin);

        return username;
    }

    private async Task<string> SeedTechnicianUserAsync(OrgId org, TechnicianId technicianId)
    {
        const string username = "field@vance.example";
        await _factory.SeedUserAsync(org, username, "boiler-service-call", UserRole.Technician, technicianId);

        return username;
    }

    /// <summary>
    /// Writes a technician straight through EF — <c>POST /technicians</c> is <c>AdminOnly</c>, and
    /// this flow needs the id before it can seed the linked login anyway.
    /// </summary>
    private async Task<TechnicianId> SeedTechnicianAsync(OrgId org)
    {
        var technician = TechnicianBuilder.Any().ForOrg(org).Named("Alex Rivera").Skilled("hvac").Build();

        await using var context = _postgres.NewContext(org);
        context.Technicians.Add(technician);
        await context.SaveChangesAsync();

        return technician.Id;
    }

    private static async Task<JobResponse> ABookedJobAsync(HttpClient client, string adminToken)
    {
        var customer = await PostAsync<CustomerSummaryResponse>(
            client, adminToken, "/customers", new CreateCustomerRequest("Vance Refrigeration", null, null));
        var location = await PostAsync<ServiceLocationResponse>(
            client,
            adminToken,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Head office", "1 High Street, London", 51.5074d, -0.1278d));

        return await PostAsync<JobResponse>(
            client,
            adminToken,
            "/jobs",
            new CreateJobRequest(
                customer.Id, location.Id, "hvac", JobPriority.Normal,
                MorningOf.AddHours(1), MorningOf.AddHours(4), TimeSpan.FromHours(1)));
    }

    private static async Task<TResponse> PostAsync<TResponse>(
        HttpClient client, string token, string path, object body)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path).Authorized(token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }
}
