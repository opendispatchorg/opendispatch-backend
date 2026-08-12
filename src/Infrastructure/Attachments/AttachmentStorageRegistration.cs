using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Attachments;

/// <summary>
/// Registration for attachment content storage.
/// </summary>
public static class AttachmentStorageRegistration
{
    /// <summary>
    /// Registers the local-disk adapter over a root directory.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="root">
    /// Reads the directory once the container is built, for the same reason the connection string
    /// is a factory: the host validates its configuration on start, and the validated value does
    /// not exist yet when registration runs.
    /// </param>
    /// <remarks>
    /// A singleton, because it holds a path and nothing else — no connection, no per-request state,
    /// and the filesystem is perfectly happy being written to from several requests at once. The
    /// bucket adapter that replaces this later is registered the same way and the line above it is
    /// the only thing that changes.
    /// </remarks>
    public static IServiceCollection AddLocalAttachmentStorage(
        this IServiceCollection services,
        Func<IServiceProvider, string> root) =>
        services.AddSingleton<IAttachmentStorage>(provider => new LocalDiskAttachmentStorage(
            root(provider),
            provider.GetRequiredService<ILogger<LocalDiskAttachmentStorage>>()));
}
