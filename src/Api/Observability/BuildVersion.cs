using System.Reflection;

namespace OpenDispatch.Api.Observability;

/// <summary>
/// Which build this is: a version number and the commit it was built from.
/// </summary>
/// <remarks>
/// <para>
/// The one property of a running container that cannot be discovered by looking at it, and the one
/// that makes every incident shorter: "is the fix deployed" should be a question the process
/// answers about itself rather than an archaeology exercise across a deployment log, an image tag
/// and somebody's memory of which build went out.
/// </para>
/// <para>
/// Read from <see cref="AssemblyInformationalVersionAttribute"/>, which the SDK writes as
/// <c>{Version}+{SourceRevisionId}</c> — so one attribute carries both halves and there is no
/// generated file to keep in step. A build that was not given a revision says <c>local</c>, which
/// is the honest answer for a working tree that may contain anything.
/// </para>
/// </remarks>
public static class BuildVersion
{
    /// <summary>What a build with no revision stamped on it reports.</summary>
    public const string UnknownRevision = "local";

    private static readonly (string Version, string Revision) Stamp = Read();

    /// <summary>The release number — <c>0.1.0</c>.</summary>
    public static string Version => Stamp.Version;

    /// <summary>The commit this was built from, or <see cref="UnknownRevision"/>.</summary>
    public static string Revision => Stamp.Revision;

    /// <summary>Both, as one string: <c>0.1.0+abc1234</c>.</summary>
    public static string Full => $"{Version}+{Revision}";

    private static (string, string) Read()
    {
        var informational = typeof(BuildVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return ("unknown", UnknownRevision);
        }

        // The SDK's own separator. Split on the first, because a semantic version may carry a
        // build-metadata '+' of its own in principle and the revision is what comes last.
        var separator = informational.IndexOf('+', StringComparison.Ordinal);

        return separator < 0
            ? (informational, UnknownRevision)
            : (informational[..separator], informational[(separator + 1)..]);
    }
}
