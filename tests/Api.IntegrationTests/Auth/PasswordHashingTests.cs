using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Auth;

/// <summary>
/// What the hasher does with a stored value it cannot read.
/// </summary>
/// <remarks>
/// <para>
/// The happy path is covered wherever a login is: every auth flow in this suite hashes a password
/// and verifies it. What was not covered — and what the correctness pass found — is the other side:
/// <c>Verify</c> decoded both base64 fields without checking them, so a row corrupted by a bad
/// migration, a truncated column or a hand-edited database threw a <c>FormatException</c> out of the
/// login path and answered a 500.
/// </para>
/// <para>
/// There is no case where the right answer to "does this password match?" is a stack trace. It is
/// no, every time, and the deployment's problem is visible in the row rather than in a broken
/// endpoint.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class PasswordHashingTests
{
    private readonly PostgresFixture _postgres;

    public PasswordHashingTests(PostgresFixture postgres) => _postgres = postgres;

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("100000.not-base64!.also-not-base64!")]
    [InlineData("100000.c2FsdA==")]
    [InlineData("nonsense.c2FsdA==.a2V5")]
    [InlineData("0.c2FsdA==.a2V5")]
    [InlineData("100000.c2FsdA==.")]
    public void RefusesAPasswordAgainstAHashItCannotRead(string stored)
    {
        Assert.False(Hasher().Verify("riverside-heating", stored));
    }

    /// <summary>And the ordinary case still works, both ways round.</summary>
    [Fact]
    public void VerifiesAPasswordAgainstItsOwnHash()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("riverside-heating");

        Assert.True(hasher.Verify("riverside-heating", hash));
        Assert.False(hasher.Verify("riverside-heatinh", hash));
    }

    private IPasswordHasher Hasher()
    {
        var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

        return services.GetRequiredService<IPasswordHasher>();
    }
}
