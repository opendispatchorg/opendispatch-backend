using OpenDispatch.Application.Auth;

namespace OpenDispatch.Api.Users;

/// <summary>
/// Everything <c>create-user</c> has to say, source-generated.
/// </summary>
/// <remarks>
/// Like <c>SeedLog</c>, these lines are a user interface rather than telemetry: the verb has no
/// other output, and what it prints is how an operator finds out whether they now have a login. It
/// never prints the password — unlike the demo seeder's, this one is real.
/// </remarks>
internal static partial class CreateUserLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Usage: create-user --username <name> --org <organization> --role <Admin|Dispatcher|Technician> "
            + "[--technician <guid>] [--password <password>]. The password is read from standard input if it is "
            + "not given, which is the way to keep it out of your shell history.")]
    internal static partial void Usage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "'{Role}' is not a role. The roles are: {Roles}.")]
    internal static partial void UnknownRole(ILogger logger, string role, string roles);

    [LoggerMessage(Level = LogLevel.Error, Message = "'{Technician}' is not a technician id.")]
    internal static partial void UnreadableTechnician(ILogger logger, string technician);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No technician with that id works for {Organization}, so a login for them would answer for "
            + "nobody. Create the technician first, or leave --technician off.")]
    internal static partial void NoSuchTechnician(ILogger logger, string organization);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "--technician belongs on a Technician login. An Admin or a Dispatcher is not anybody's field "
            + "identity.")]
    internal static partial void TechnicianOnAnOfficeLogin(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A password must be at least {Minimum} characters.")]
    internal static partial void PasswordTooShort(ILogger logger, int minimum);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refusing to create the login: {Reason}")]
    internal static partial void Refused(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password (not shown):")]
    internal static partial void AskingForPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered a new organization, {Organization}.")]
    internal static partial void OrganizationRegistered(ILogger logger, string organization);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created {Username} in {Organization} as {Role}.")]
    internal static partial void Created(ILogger logger, string username, string organization, UserRole role);

    /// <remarks>
    /// Says "replaced" rather than "created" because it is the only password reset this system has,
    /// and an operator who mistyped a username needs to know they have just rewritten somebody.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Replaced the existing login {Username}: it is now in {Organization} as {Role}, with the "
            + "password just given.")]
    internal static partial void Replaced(ILogger logger, string username, string organization, UserRole role);
}
