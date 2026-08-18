using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Provisioning;

namespace OpenDispatch.Api.Users;

/// <summary>
/// <c>dotnet OpenDispatch.Api.dll create-user …</c>: the host built for its configuration and its
/// container, run once to write one login, and exited without ever listening.
/// </summary>
/// <remarks>
/// <para>
/// The answer to "a freshly migrated database has no users and no endpoint creates one". A verb
/// rather than an HTTP route because the first administrator cannot authenticate to ask for
/// themselves, and an anonymous endpoint that mints administrators is the last thing this API
/// should own.
/// </para>
/// <para>
/// Same shape as <c>SeedCommand</c>, and deliberately not the same guard: seeding is only allowed
/// in Development, while creating a login is what a production host does once, on purpose, from
/// somebody's terminal or a deployment job.
/// </para>
/// <para>
/// <strong>The password is read from standard input when it is not passed.</strong> An argument is
/// in the shell's history, in <c>ps</c>, and in whatever log a CI runner keeps of its commands, so
/// the flag exists for scripts that already hold the secret (piping from a secrets manager) and the
/// prompt is what a person should use.
/// </para>
/// </remarks>
internal static class CreateUserCommand
{
    /// <summary>The argument that asks for a login rather than a server.</summary>
    private const string Verb = "create-user";

    /// <summary>
    /// The shortest password this will write.
    /// </summary>
    /// <remarks>
    /// A floor rather than a policy: there is no rotation, no history and no complexity rule here,
    /// and pretending otherwise would be theatre. Twelve characters is what makes the deliberately
    /// slow hash (100,000 PBKDF2 iterations) worth having.
    /// </remarks>
    private const int MinimumPasswordLength = 12;

    /// <summary>Whether this process was started to create a login rather than to serve.</summary>
    /// <param name="args">The host's command-line arguments.</param>
    public static bool Requested(string[] args) => Array.Exists(args, argument =>
        string.Equals(argument, Verb, StringComparison.Ordinal));

    /// <summary>Creates the login the arguments describe.</summary>
    /// <param name="app">The built host, for its services.</param>
    /// <param name="args">The host's command-line arguments.</param>
    /// <returns>The process exit code: zero if a login was written, one if it was refused.</returns>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var username = Argument(args, "--username");
        var organization = Argument(args, "--org");
        var role = Argument(args, "--role");

        if (username is null || organization is null || role is null)
        {
            CreateUserLog.Usage(app.Logger);
            return 1;
        }

        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole) || !Enum.IsDefined(parsedRole))
        {
            CreateUserLog.UnknownRole(app.Logger, role, string.Join(", ", Enum.GetNames<UserRole>()));
            return 1;
        }

        TechnicianId? technician = null;

        if (Argument(args, "--technician") is { } named)
        {
            if (!Guid.TryParse(named, out var technicianId))
            {
                CreateUserLog.UnreadableTechnician(app.Logger, named);
                return 1;
            }

            technician = TechnicianId.From(technicianId);
        }

        var password = Argument(args, "--password") ?? Prompt(app);

        if (password is null || password.Length < MinimumPasswordLength)
        {
            CreateUserLog.PasswordTooShort(app.Logger, MinimumPasswordLength);
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<UserProvisioner>();

        try
        {
            var (provisioned, refusal) = await provisioner
                .ProvisionAsync(username, password, parsedRole, organization, technician, CancellationToken.None)
                .ConfigureAwait(false);

            if (provisioned is null)
            {
                Refuse(app, refusal, organization);
                return 1;
            }

            if (provisioned.OrganizationCreated)
            {
                CreateUserLog.OrganizationRegistered(app.Logger, provisioned.Organization);
            }

            if (provisioned.Replaced)
            {
                CreateUserLog.Replaced(app.Logger, provisioned.Username, provisioned.Organization, provisioned.Role);
            }
            else
            {
                CreateUserLog.Created(app.Logger, provisioned.Username, provisioned.Organization, provisioned.Role);
            }

            return 0;
        }
        catch (DomainException refused)
        {
            // The one thing the domain refuses on this path: an organization with no name.
            CreateUserLog.Refused(app.Logger, refused.Message);
            return 1;
        }
    }

    private static void Refuse(WebApplication app, ProvisioningRefusal refusal, string organization)
    {
        switch (refusal)
        {
            case ProvisioningRefusal.NoSuchTechnician:
                CreateUserLog.NoSuchTechnician(app.Logger, organization);
                break;
            case ProvisioningRefusal.TechnicianOnAnOfficeLogin:
                CreateUserLog.TechnicianOnAnOfficeLogin(app.Logger);
                break;
            case ProvisioningRefusal.UsernameBelongsToAnotherOrganization:
                CreateUserLog.UsernameBelongsElsewhere(app.Logger, organization);
                break;
            default:
                CreateUserLog.Refused(app.Logger, "the request was refused.");
                break;
        }
    }

    /// <summary>
    /// Reads a password typed at the terminal, without echoing it.
    /// </summary>
    /// <remarks>
    /// Falls back to a plain read when there is no terminal — a piped secret, which is how a
    /// deployment job would do this — because <c>Console.ReadKey</c> throws when input is
    /// redirected.
    /// </remarks>
    private static string? Prompt(WebApplication app)
    {
        CreateUserLog.AskingForPassword(app.Logger);

        if (Console.IsInputRedirected)
        {
            return Console.ReadLine();
        }

        var typed = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key is ConsoleKey.Enter)
            {
                Console.WriteLine();
                return typed.ToString();
            }

            if (key.Key is ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }

    /// <summary>The value of <paramref name="name"/>, as <c>--name value</c>.</summary>
    private static string? Argument(string[] args, string name)
    {
        var at = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.Ordinal));

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
