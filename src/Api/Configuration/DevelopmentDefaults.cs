namespace OpenDispatch.Api.Configuration;

/// <summary>
/// The credentials this repository commits so that a fresh clone runs with no setup — and the one
/// place that knows they are not credentials at all.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A committed secret is only safe while it cannot reach production.</strong>
/// <c>appsettings.json</c> ships a signing key and a database password because
/// <c>make up &amp;&amp; make run</c> has to work on a machine that has configured nothing, and that
/// convenience is worth keeping. What is not acceptable is a deployment inheriting them by
/// forgetting to override them: the signing key is long enough to satisfy
/// <see cref="JwtOptions"/>' own length rule, so without this check a production host starts
/// happily and signs tokens that anybody who has read this public repository can forge.
/// </para>
/// <para>
/// The check is deliberately exact rather than heuristic. It answers one question — "is this the
/// value we shipped?" — and a deployment that has set anything of its own passes it without ever
/// knowing this type exists.
/// </para>
/// </remarks>
public static class DevelopmentDefaults
{
    /// <summary>The signing key committed in <c>appsettings.json</c>.</summary>
    public const string JwtSigningKey = "local-dev-signing-key-do-not-use-in-production-change-me";

    /// <summary>
    /// The database password committed in <c>appsettings.json</c> and <c>docker-compose.yml</c>.
    /// </summary>
    /// <remarks>
    /// Matched inside the connection string rather than comparing the whole string: a deployment
    /// that pointed the committed password at its own host has made exactly the mistake this
    /// guard exists for, and an exact-match check would wave it through.
    /// </remarks>
    public const string DatabasePassword = "Password=opendispatch";

    /// <summary>
    /// Whether an environment may use them: the two this repository controls, and no others.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than "anything but Production", so an environment nobody thought about
    /// — <c>Staging</c>, a customer's own name for their host — is refused by default instead of
    /// being exposed by omission. <c>Testing</c> is here because <c>ApiFactory</c> boots the real
    /// host under that name and overrides the connection string but not the signing key; a test
    /// suite that had to carry its own key would be testing a host nobody runs.
    /// </remarks>
    public static bool AreAllowedIn(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironmentName);
    }

    /// <summary>The environment name <c>ApiFactory</c> boots the host under.</summary>
    internal const string TestingEnvironmentName = "Testing";
}
