using Npgsql;
using OpenDispatch.Api.Configuration;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Configuration;

/// <summary>
/// A database URL, turned into something Npgsql will read.
/// </summary>
/// <remarks>
/// <para>
/// Worth testing thoroughly for a reason that is about deployment rather than about code: Render,
/// Fly, Heroku, Supabase, Neon and Railway all hand out a <c>postgres://</c> URL, Npgsql accepts
/// only key-value pairs, and the failure is an <c>ArgumentException</c> about "the format of the
/// initialization string" that names neither the setting nor the platform. It is the first thing a
/// deployment meets and the least helpful message it will ever get.
/// </para>
/// <para>
/// Every case is checked by handing the result to <see cref="NpgsqlConnectionStringBuilder"/> —
/// the thing that will actually read it — rather than by comparing strings. A test that asserted
/// the exact text would pass while producing something Npgsql rejects, which is the only failure
/// that matters here.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void TranslatesTheUrlAPlatformHandsYou()
    {
        var translated = PostgresConnectionString.Normalize(
            "postgres://opendispatch:s3cret@dpg-abc123-a.frankfurt-postgres.render.com:5432/opendispatch_x7k2");

        var read = new NpgsqlConnectionStringBuilder(translated);

        Assert.Equal("dpg-abc123-a.frankfurt-postgres.render.com", read.Host);
        Assert.Equal(5432, read.Port);
        Assert.Equal("opendispatch_x7k2", read.Database);
        Assert.Equal("opendispatch", read.Username);
        Assert.Equal("s3cret", read.Password);
    }

    /// <summary>Both spellings, because platforms disagree about which one they emit.</summary>
    [Theory]
    [InlineData("postgres://u:p@db.example/opendispatch")]
    [InlineData("postgresql://u:p@db.example/opendispatch")]
    [InlineData("POSTGRES://u:p@db.example/opendispatch")]
    public void ReadsEitherScheme(string url)
    {
        var read = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(url));

        Assert.Equal("db.example", read.Host);
        Assert.Equal("opendispatch", read.Database);
    }

    /// <summary>
    /// A URL with no port leaves the key out rather than writing 5432, so Npgsql applies its own
    /// default — which is more honest than this class inventing a number the deployment did not
    /// give, and is the same number anyway.
    /// </summary>
    [Fact]
    public void LeavesThePortToNpgsqlWhenTheUrlDoesNotSayOne()
    {
        var translated = PostgresConnectionString.Normalize("postgres://u:p@db.example/opendispatch");

        Assert.DoesNotContain("Port=", translated, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(translated).Port);
    }

    /// <summary>
    /// How a managed platform says TLS is required, and the parameter most likely to be on the end
    /// of a real URL. Npgsql reads the same names libpq does, so they carry across unchanged.
    /// </summary>
    [Fact]
    public void CarriesQueryParametersAcross()
    {
        var translated = PostgresConnectionString.Normalize(
            "postgres://u:p@db.example/opendispatch?sslmode=require");

        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(translated).SslMode);
    }

    /// <summary>
    /// A generated password full of URL-significant characters is the ordinary case, not an edge
    /// one — and a password passed through unescaped authenticates as something else.
    /// </summary>
    [Fact]
    public void UnescapesCredentials()
    {
        var translated = PostgresConnectionString.Normalize(
            "postgres://open%40dispatch:p%40ss%2Fw%3Ard@db.example/opendispatch");

        var read = new NpgsqlConnectionStringBuilder(translated);

        Assert.Equal("open@dispatch", read.Username);
        Assert.Equal("p@ss/w:rd", read.Password);
    }

    /// <summary>
    /// The other half of the contract: a deployment that already has an Npgsql connection string —
    /// which is every existing one, including the compose database and every test in this suite —
    /// gets it back untouched.
    /// </summary>
    [Fact]
    public void LeavesAnOrdinaryConnectionStringAlone()
    {
        const string Configured =
            "Host=localhost;Port=5433;Database=opendispatch;Username=opendispatch;Password=opendispatch";

        Assert.Equal(Configured, PostgresConnectionString.Normalize(Configured));
    }

    /// <summary>
    /// Loud rather than approximate. A URL this cannot read is a deployment about to connect
    /// somewhere unintended, and guessing is the one thing worse than refusing.
    /// </summary>
    [Theory]
    [InlineData("postgres://u:p@db.example")]
    [InlineData("postgres://u:p@db.example/")]
    [InlineData("postgres://")]
    public void RefusesAUrlItCannotRead(string url)
    {
        Assert.Throws<ArgumentException>(() => PostgresConnectionString.Normalize(url));
    }
}
