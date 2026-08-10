using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Pipeline;

/// <summary>
/// The transaction behavior against a real database.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests pin what the pipeline <em>does</em> — begin, save, commit or roll back, in
/// that order. Only Postgres can say whether a rollback actually takes rows back, and that is
/// the whole claim the behavior makes: a command that fails leaves nothing behind even when it
/// had already written.
/// </para>
/// <para>
/// It goes through the real registrations, so the transaction is EF's, the repositories are the
/// step-28 ones, and the tenant is resolved the way a request will resolve it.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class TransactionBehaviorTests
{
    // Its own organization, so the tenant filters make "what this test wrote" and "every
    // customer this context can see" the same set.
    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public TransactionBehaviorTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task KeepsWhatASuccessfulCommandWrote()
    {
        await using var services = BuildPipeline();
        using var scope = Acting(services);

        var result = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new TakeOnCustomerCommand("Ivy Fabrication", ThenRefuse: false));

        Assert.True(result.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        var customer = Assert.Single(await context.Customers.ToListAsync());
        Assert.Equal(result.Value, customer.Id);
    }

    /// <summary>
    /// The one that matters. The handler wrote the row and saved it before deciding to fail, so
    /// the row existed in the database when the failure was reported — and is gone afterwards.
    /// </summary>
    [Fact]
    public async Task TakesBackWhatAFailedCommandHadAlreadyWritten()
    {
        await using var services = BuildPipeline();
        using var scope = Acting(services);

        var result = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new TakeOnCustomerCommand("Ivy Fabrication", ThenRefuse: true));

        Assert.Equal(TakeOnCustomerHandler.Refused, result.Error);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Empty(await context.Customers.ToListAsync());
    }

    /// <summary>
    /// The real composition plus the one sample handler. Nothing about the pipeline itself is
    /// restated here, so a behavior registered in the wrong order fails these too.
    /// </summary>
    private ServiceProvider BuildPipeline() =>
        TestHost.Over(_postgres)
            .AddTransient<IRequestHandler<TakeOnCustomerCommand, Result<CustomerId>>, TakeOnCustomerHandler>()
            .BuildServiceProvider(validateScopes: true);

    private IServiceScope Acting(ServiceProvider services) => services.ActingAs(_tenant);
}
