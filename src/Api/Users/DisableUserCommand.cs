using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Api.Users;

/// <summary>
/// <c>dotnet OpenDispatch.Api.dll disable-user --username …</c>: the way out that
/// <c>create-user</c> had no counterpart for.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes is an employee leaving. Until now the only way to stop somebody signing in
/// was to delete their row by hand in psql — which also detached every audit entry that names them,
/// so the tidier the operator, the less the shop could answer about its own history.
/// </para>
/// <para>
/// <strong>It switches the login off; it does not revoke the token already in their phone.</strong>
/// Authorization here is a signed JWT and nothing reads the user store per request, so somebody
/// disabled at nine can keep calling until their token expires — bounded by <c>Jwt:ExpiryMinutes</c>
/// and no longer. Closing that gap properly means a per-request store lookup or short tokens plus
/// refresh, which is a real design decision rather than a line of code; the README says so where an
/// operator will read it, so nobody discovers it during an incident.
/// </para>
/// <para>
/// There is no <c>enable-user</c>. Re-running <c>create-user</c> for the same username switches the
/// login back on and sets a new password, which is the only shape a return to work should have.
/// </para>
/// </remarks>
internal static class DisableUserCommand
{
    /// <summary>The argument that asks for a login to be switched off rather than for a server.</summary>
    private const string Verb = "disable-user";

    /// <summary>Whether this process was started to disable a login rather than to serve.</summary>
    /// <param name="args">The host's command-line arguments.</param>
    public static bool Requested(string[] args) => Array.Exists(args, argument =>
        string.Equals(argument, Verb, StringComparison.Ordinal));

    /// <summary>Disables the login the arguments name.</summary>
    /// <param name="app">The built host, for its services.</param>
    /// <param name="args">The host's command-line arguments.</param>
    /// <returns>The process exit code: zero if a login was switched off, one if there was none.</returns>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var at = Array.FindIndex(args, argument => string.Equals(argument, "--username", StringComparison.Ordinal));
        var username = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;

        if (string.IsNullOrWhiteSpace(username))
        {
            DisableUserLog.Usage(app.Logger);
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();

        if (!await users.SetActiveAsync(username, active: false, CancellationToken.None).ConfigureAwait(false))
        {
            DisableUserLog.NoSuchUser(app.Logger, username);
            return 1;
        }

        DisableUserLog.Disabled(app.Logger, username);

        return 0;
    }
}

/// <summary>
/// Everything <c>disable-user</c> has to say, source-generated — a user interface rather than
/// telemetry, like <see cref="CreateUserLog"/>.
/// </summary>
internal static partial class DisableUserLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Usage: disable-user --username <name>. The login stops working; the user stays, so the audit "
            + "trail can still say what they did.")]
    internal static partial void Usage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "There is no login for '{Username}'.")]
    internal static partial void NoSuchUser(ILogger logger, string username);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Disabled the login for {Username}. Any token they already hold works until it expires "
            + "(Jwt:ExpiryMinutes).")]
    internal static partial void Disabled(ILogger logger, string username);
}
