using System.Diagnostics.Metrics;

namespace OpenDispatch.Application.Observability;

/// <summary>
/// How sign-in is going.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The one thing nothing else in this system could see.</strong> The audit trail records
/// commands that changed something, deliberately — a refused sign-in changed nothing, and a trail
/// full of attempts would be noise holding personal data. But that left repeated failed sign-ins
/// invisible: the only signal was the rate limiter's own rejection counter, which fires when a cap
/// is <em>hit</em> and says nothing about the hundred attempts underneath it. A shop being worked
/// through by somebody with a password list, staying below twenty per five minutes, produced no
/// number at all.
/// </para>
/// <para>
/// <strong>The reason is a dimension, and it is server-side only.</strong> <c>LoginHandler</c>
/// answers every failure with one error, deliberately, so a caller cannot tell an unknown username
/// from a wrong password from a disabled account — that is what stops the endpoint being a
/// user-enumeration oracle. Here the distinction is safe and load-bearing: a spike in
/// <c>disabled</c> is somebody who left still trying, a spike in <c>unknown</c> is a list being
/// worked through, and a spike in <c>password</c> is either an attack or a shop that has just
/// changed something. An alert that could not tell them apart would page for all three.
/// </para>
/// <para>
/// No username is recorded. The cardinality would be unbounded and the value would be personal
/// data; the count and the reason are what an alert needs, and the log line beside it is where a
/// human looks next.
/// </para>
/// </remarks>
public sealed class AuthMetrics
{
    /// <summary>The instrument name counting refused sign-ins.</summary>
    public const string SignInFailuresName = "opendispatch.auth.signin.failures";

    /// <summary>The tag carrying why a sign-in was refused.</summary>
    public const string ReasonTag = "auth.failure.reason";

    /// <summary>No login by that name.</summary>
    public const string UnknownUser = "unknown";

    /// <summary>The login exists and the password was wrong.</summary>
    public const string WrongPassword = "password";

    /// <summary>The login exists and has been switched off.</summary>
    public const string DisabledUser = "disabled";

    private readonly Counter<long> _failures;

    /// <summary>Creates the instrument on the shared <see cref="OpenDispatchMetrics.MeterName"/> meter.</summary>
    /// <param name="meters">The host's meter factory — see <see cref="SchedulingMetrics"/> for why it is injected.</param>
    public AuthMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);

        _failures = meters.Create(OpenDispatchMetrics.MeterName).CreateCounter<long>(
            SignInFailuresName,
            unit: "{attempt}",
            description: "Sign-in attempts this server refused, by reason.");
    }

    /// <summary>Records a refused sign-in.</summary>
    /// <param name="reason">One of the constants on this type.</param>
    public void SignInFailed(string reason) =>
        _failures.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));
}
