using System.Globalization;
using System.Text;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// Accepts a database URL as well as an Npgsql connection string.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Npgsql does not accept a <c>postgres://</c> URI, and almost every managed platform hands
/// you one.</strong> Render, Heroku, Fly, Supabase, Neon and Railway all expose their database as a
/// URL, because that is what libpq and every other ecosystem's driver reads. Npgsql wants
/// key-value pairs. A deployment that pastes what its platform gave it gets
/// <c>ArgumentException: Format of the initialization string does not conform to specification</c>
/// at startup, which names nothing useful and costs an afternoon at exactly the wrong moment.
/// </para>
/// <para>
/// So this translates one shape into the other and leaves the other alone. It is at the edge, in
/// configuration, rather than in the persistence layer: what a platform hands a deployment is a
/// fact about the deployment, and <c>AddInfrastructure</c> should keep receiving a connection
/// string it can use unmodified.
/// </para>
/// <para>
/// <strong>Not a general URI parser.</strong> It reads exactly what these platforms emit — scheme,
/// credentials, host, optional port, database, and any query parameters, which is how
/// <c>?sslmode=require</c> arrives — and refuses anything it cannot read rather than guessing. A
/// wrong translation would be worse than no translation: it would connect to something.
/// </para>
/// </remarks>
public static class PostgresConnectionString
{
    /// <summary>The two schemes a platform uses for the same thing.</summary>
    private static readonly string[] Schemes = ["postgres", "postgresql"];

    /// <summary>
    /// The value as Npgsql wants it, translating a database URL if that is what was given.
    /// </summary>
    /// <param name="configured">Whatever the deployment put in <c>Database:ConnectionString</c>.</param>
    /// <returns>An Npgsql key-value connection string.</returns>
    /// <exception cref="ArgumentException">
    /// The value looks like a database URL and is not one this can read — a missing host, a missing
    /// database name. Loud at startup, rather than a connection to somewhere unintended.
    /// </exception>
    public static string Normalize(string configured)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configured);

        if (!LooksLikeUrl(configured))
        {
            return configured;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var url))
        {
            throw new ArgumentException(
                "Database:ConnectionString starts with a postgres:// scheme but is not a valid URL.",
                nameof(configured));
        }

        var database = url.AbsolutePath.Trim('/');

        if (string.IsNullOrEmpty(url.Host) || string.IsNullOrEmpty(database))
        {
            throw new ArgumentException(
                "Database:ConnectionString is a postgres:// URL with no host or no database name.",
                nameof(configured));
        }

        var builder = new StringBuilder()
            .Append("Host=").Append(url.Host)
            .Append(";Database=").Append(Unescape(database));

        // -1 is "not specified", which is what Uri reports for a URL with no port. Npgsql's own
        // default is 5432 and it applies it itself, so leaving the key out is more honest than
        // writing a number the deployment did not give.
        if (url.Port > 0)
        {
            builder.Append(";Port=").Append(url.Port.ToString(CultureInfo.InvariantCulture));
        }

        var credentials = url.UserInfo.Split(':', 2);

        if (credentials is [{ Length: > 0 } user, ..])
        {
            builder.Append(";Username=").Append(Unescape(user));
        }

        if (credentials is [_, { Length: > 0 } password])
        {
            builder.Append(";Password=").Append(Unescape(password));
        }

        // Everything after the '?' — sslmode above all, which is how a managed platform says TLS is
        // required. Npgsql understands the same names libpq does, so they carry across unchanged.
        foreach (var parameter in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = parameter.Split('=', 2);

            if (pair is [{ Length: > 0 } key, { } value])
            {
                builder.Append(';').Append(Unescape(key)).Append('=').Append(Unescape(value));
            }
        }

        return builder.ToString();
    }

    /// <summary>Whether this is a database URL rather than a connection string.</summary>
    /// <remarks>
    /// Matched on the scheme rather than on the absence of <c>=</c>, because a password can contain
    /// anything and a heuristic over the whole value is how one of these gets misread.
    /// </remarks>
    private static bool LooksLikeUrl(string value) =>
        Schemes.Any(scheme => value.StartsWith($"{scheme}://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Percent-decodes a component. Passwords with <c>@</c>, <c>/</c> or <c>:</c> in them arrive
    /// escaped, and Npgsql wants the real characters.
    /// </summary>
    private static string Unescape(string value) => Uri.UnescapeDataString(value);
}
