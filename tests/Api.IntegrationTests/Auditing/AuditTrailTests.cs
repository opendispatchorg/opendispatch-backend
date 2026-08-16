using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Behaviors;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Results;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Auditing;

/// <summary>
/// The trail against a real database and a real request.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests pin what the behavior records. What only Postgres can settle is the claim its
/// position in the pipeline makes: the entry is written by the same transaction as the work it
/// describes, so a command whose commit fails leaves no record of having succeeded. A trail that can
/// disagree with the data is worse than no trail, because it would be believed.
/// </para>
/// <para>
/// And only a real request can settle who an entry names: the actor is resolved at the edge from the
/// same principal the tenant comes from, and an in-process scope has no principal.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AuditTrailTests : IClassFixture<ApiFactory>
{
    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;
    private readonly ApiFactory _factory;

    public AuditTrailTests(ApiFactory factory, PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    /// <summary>A command that lands leaves its work and its entry, both.</summary>
    [Fact]
    public async Task WritesTheEntryWithTheWorkItDescribes()
    {
        var job = JobId.New();

        await using var services = BuildPipeline();
        using var scope = services.ActingAs(_tenant);

        var result = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new PlanStopCommand(job, ThenCollide: false));

        Assert.True(result.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Single(await context.Assignments.ToListAsync());

        var entry = Assert.Single(await context.AuditEntries.ToListAsync());
        Assert.Equal("PlanStop", entry.Action);
        Assert.Equal(_tenant, entry.OrgId);
        Assert.Equal(job.Value, Targets(entry.Targets).Values.Single());
    }

    /// <summary>
    /// The one that matters, and the reason the behavior sits innermost. The handler succeeded, so
    /// an entry was staged; the save that followed refused a second stop for a job that already had
    /// one, so the work went back. Written anywhere but inside the transaction, the trail would now
    /// say a stop was planned that nobody can find.
    /// </summary>
    [Fact]
    public async Task LosesTheEntryWithWorkThatRolledBack()
    {
        var job = JobId.New();

        await using var services = BuildPipeline();
        using var scope = services.ActingAs(_tenant);

        var result = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new PlanStopCommand(job, ThenCollide: true));

        // Not a refusal the handler decided on — it returned success, and the database refused the
        // row underneath it, which is what makes the entry already staged by then.
        Assert.Equal(ConcurrencyErrors.Duplicate(), result.Error);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Empty(await context.Assignments.ToListAsync());
        Assert.Empty(await context.AuditEntries.ToListAsync());
    }

    /// <summary>
    /// Over HTTP, because the actor is resolved at the edge: two writes by a signed-in dispatcher
    /// leave two entries naming them and the customer they touched.
    /// </summary>
    [Fact]
    public async Task NamesTheUserWhoMadeTheChangeAndWhatTheyTouched()
    {
        var org = OrgId.New();

        // A username of this test's own: seeding is an upsert, so a name another class also uses
        // would come back carrying that class's user id.
        var username = $"audit-{Guid.NewGuid():N}@vance.example";
        var user = await _factory.SeedUserAsync(org, username, "vance-refrigeration", UserRole.Dispatcher);

        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(username, "vance-refrigeration");

        using var creating = await SendAsync(
            client, token, HttpMethod.Post, "/customers",
            new CreateCustomerRequest("Vance Refrigeration", null, null));
        Assert.Equal(HttpStatusCode.Created, creating.StatusCode);

        var created = (await creating.Content.ReadFromJsonAsync<CustomerSummaryResponse>())!;

        using var renaming = await SendAsync(
            client, token, HttpMethod.Put, $"/customers/{created.Id}",
            new UpdateCustomerRequest("Vance Refrigeration Ltd", null, null));
        Assert.Equal(HttpStatusCode.NoContent, renaming.StatusCode);

        await using var context = _postgres.NewContext(org);
        var entries = await context.AuditEntries.OrderBy(entry => entry.At).ToListAsync();

        Assert.Equal(["CreateCustomer", "UpdateCustomer"], entries.Select(entry => entry.Action));
        Assert.All(entries, entry =>
        {
            Assert.Equal(user.Id, entry.UserId);
            Assert.Equal(username, entry.Username);
        });

        // The change names the customer it changed, so "who renamed this one" is answerable.
        Assert.Equal(created.Id, Targets(entries[^1].Targets).Values.Single());
    }

    private static Dictionary<string, Guid> Targets(string targets) =>
        JsonSerializer.Deserialize<Dictionary<string, Guid>>(targets)!;

    /// <summary>The real composition plus the one sample handler, as the pipeline tests do.</summary>
    private ServiceProvider BuildPipeline() =>
        TestHost.Over(_postgres)
            .AddTransient<IRequestHandler<PlanStopCommand, Result<AssignmentId>>, PlanStopHandler>()
            .BuildServiceProvider(validateScopes: true);

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path).Authorized(token);
        request.Content = JsonContent.Create(body);

        return client.SendAsync(request);
    }
}

/// <summary>
/// A sample command that plans a stop and then, if told to, plans the same job a second time.
/// </summary>
/// <remarks>
/// The collision is how a test reaches the case that matters and cannot be arranged from outside: a
/// handler that <em>succeeded</em>, so the entry is staged, over a transaction that then fails. A
/// refusal the handler returned would not do, because the behavior never records one. Two stops for
/// one job is also the real shape of this failure — two dispatchers assigning at once, which the
/// unique index on <c>assignments.job_id</c> exists to refuse.
/// </remarks>
/// <param name="Job">The job to plan a stop for.</param>
/// <param name="ThenCollide">Whether to stage a second stop for that same job.</param>
internal sealed record PlanStopCommand(JobId Job, bool ThenCollide) : ICommand<AssignmentId>;

/// <summary>Plans, saves, and then possibly stages a stop that cannot be saved.</summary>
internal sealed class PlanStopHandler(
    IAssignmentRepository assignments,
    IUnitOfWork unitOfWork,
    ITenantContext tenant)
    : IRequestHandler<PlanStopCommand, Result<AssignmentId>>
{
    public async Task<Result<AssignmentId>> Handle(
        PlanStopCommand command,
        CancellationToken cancellationToken)
    {
        var stop = AssignmentBuilder.Any().ForOrg(tenant.OrgId).ForJob(command.Job).Build();
        assignments.Add(stop);

        // Saved here so the row is genuinely in the database before the failure — the same reason
        // TakeOnCustomerHandler saves: otherwise "it is not there afterwards" would also be true of
        // a pipeline that never wrote it at all.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (command.ThenCollide)
        {
            assignments.Add(AssignmentBuilder.Any().ForOrg(tenant.OrgId).ForJob(command.Job).Build());
        }

        // Success either way: what fails is the pipeline's own save, after the entry is staged.
        return Result.Success(stop.Id);
    }
}
