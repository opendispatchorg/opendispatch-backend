using System.Globalization;
using OpenDispatch.Infrastructure.Provisioning;

namespace OpenDispatch.Api.Operations;

/// <summary>
/// <c>dotnet OpenDispatch.Api.dll prune [--days N]</c>: deletes sync bookkeeping that has outlived
/// its purpose, then exits.
/// </summary>
/// <remarks>
/// <para>
/// The op log and the removal notes are the only two tables in this system that grow without
/// anything ever removing a row, and neither is business data — they are the protocol's memory of
/// what devices have already done and what has already gone. Nothing else here is prunable: a job,
/// an invoice and a photograph are the shop's records, and Document 1's promise is that the shop
/// keeps them.
/// </para>
/// <para>
/// A verb run from cron rather than a background service, because a sweep inside the host runs once
/// per replica. What it deleted is on stdout and in the exit code, so a scheduler can watch it.
/// </para>
/// </remarks>
internal static class PruneCommand
{
    /// <summary>The argument that asks for a prune rather than a server.</summary>
    private const string Verb = "prune";

    /// <summary>
    /// How much history to keep when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// Thirty days is far past the point at which a device could still be re-sending an operation —
    /// a phone that has been off for a month is doing a full resync, not replaying a queue — and
    /// short enough that the log does not become the largest table in the database.
    /// </remarks>
    private const int DefaultDays = 30;

    /// <summary>Whether this process was started to prune rather than to serve.</summary>
    /// <param name="args">The host's command-line arguments.</param>
    public static bool Requested(string[] args) => Array.Exists(args, argument =>
        string.Equals(argument, Verb, StringComparison.Ordinal));

    /// <summary>Deletes what is past the window and reports it.</summary>
    /// <param name="app">The built host, for its services.</param>
    /// <param name="args">The host's command-line arguments.</param>
    /// <returns>The process exit code: zero if it pruned, one if it could not.</returns>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var days = DefaultDays;

        if (Argument(args, "--days") is { } given)
        {
            if (!int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out days) || days < 1)
            {
                PruneLog.Usage(app.Logger, DefaultDays);

                return 1;
            }
        }

        await using var scope = app.Services.CreateAsyncScope();

        try
        {
            var pruned = await scope.ServiceProvider.GetRequiredService<SyncLogPruner>()
                .PruneAsync(TimeSpan.FromDays(days), CancellationToken.None)
                .ConfigureAwait(false);

            var before = pruned.Before.ToString("u", CultureInfo.InvariantCulture);

            PruneLog.Pruned(app.Logger, pruned.Operations, pruned.Removals, before);

            return 0;
        }
        catch (Exception failed)
        {
            PruneLog.Failed(app.Logger, failed);

            return 1;
        }
    }

    /// <summary>The value of <paramref name="name"/>, as <c>--name value</c>.</summary>
    private static string? Argument(string[] args, string name)
    {
        var at = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.Ordinal));

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}

/// <summary>What the prune verb has to say, source-generated.</summary>
internal static partial class PruneLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Usage: prune [--days N]. N is whole days of sync history to keep, at least one; "
            + "the default is {Default}.")]
    internal static partial void Usage(ILogger logger, int @default);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Pruned {Operations} sync operation(s) and {Removals} removal note(s) recorded before {Before}.")]
    internal static partial void Pruned(ILogger logger, int operations, int removals, string before);

    [LoggerMessage(Level = LogLevel.Error, Message = "The sync log could not be pruned.")]
    internal static partial void Failed(ILogger logger, Exception exception);
}
