using OpenDispatch.Application.Auth;

namespace OpenDispatch.Api.Seeding;

/// <summary>
/// Everything <c>make seed</c> and the demo-login registrar have to say, source-generated.
/// </summary>
/// <remarks>
/// Generated rather than hand-written <c>logger.Log*(...)</c> calls, for the reason
/// <c>UnhandledExceptionHandlerLog</c> gives: the templates are checked at compile time and
/// nothing boxes. Unusually for this codebase these lines are also a user interface — the seeder
/// has no other output, and what it prints is what a developer reads to find out what was loaded
/// and what to log in with.
/// </remarks>
internal static partial class SeedLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Refusing to seed. The demo dataset only loads into a {Required} host, and this one is {Actual}.")]
    internal static partial void Refused(ILogger logger, string required, string actual);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Seeded {Organization} for {Day}: {Technicians} technicians, {Customers} customers, "
            + "{Locations} service locations and {Jobs} unscheduled jobs.")]
    internal static partial void Seeded(
        ILogger logger,
        string organization,
        string day,
        int technicians,
        int customers,
        int locations,
        int jobs);

    /// <remarks>
    /// Prints a password, deliberately. These are the credentials a developer is meant to read off
    /// the console and type in, they exist only in a Development host, and they are written in the
    /// source anyway — see <c>DemoLogin</c>.
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Information, Message = "Demo login {Username} / {Password} as {Role}.")]
    internal static partial void Login(ILogger logger, string username, string password, UserRole role);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Next: `make run`, log in, then POST /schedule/optimize over {Day} to plan the day.")]
    internal static partial void NextStep(ILogger logger, string day);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No demo organization in the database, so no demo logins. Run `make seed` to load one.")]
    internal static partial void NothingToSignInTo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered {Count} demo logins for this session.")]
    internal static partial void LoginsRegistered(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The demo logins could not be registered: the database is unreachable or has not been migrated.")]
    internal static partial void LoginsUnavailable(ILogger logger, Exception exception);
}
