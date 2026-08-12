using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Attachments;

/// <summary>
/// Attachment content on the local filesystem, under one root directory.
/// </summary>
/// <remarks>
/// <para>
/// The adapter that ships, and it is a real answer rather than a placeholder: Document 1's promise
/// is that a shop can host the whole system itself, and a self-hosted deployment with a disk needs
/// nothing else. An S3 or Azure Blob adapter is one class beside this one and one changed line in
/// the registration — <c>IAttachmentStorage</c> exists precisely so that swap costs nothing above
/// it.
/// </para>
/// <para>
/// <strong>Its limits, stated rather than discovered:</strong> a single writable directory means
/// one machine, or a shared volume between machines. A deployment that runs two instances behind a
/// load balancer without shared storage will store a photograph on one and fail to find it on the
/// other. That is the point at which the bucket adapter stops being optional, and it is a
/// deployment decision rather than a code change.
/// </para>
/// <para>
/// The key is written as a nested path — <c>{root}/{org}/{attachment}</c> — so a directory listing
/// is per tenant and no single directory ends up holding every photograph a shop has ever taken.
/// Even so, every resolved path is checked to be inside the root before anything is opened: the key
/// type already makes a traversing key unconstructable, and this is the second lock on the same
/// door, because the cost of being wrong is reading arbitrary files off the host.
/// </para>
/// </remarks>
internal sealed class LocalDiskAttachmentStorage : IAttachmentStorage
{
    private readonly string _root;
    private readonly ILogger<LocalDiskAttachmentStorage> _log;

    /// <summary>Creates the adapter over a root directory, creating it if it is not there.</summary>
    /// <param name="root">Where attachment content lives.</param>
    /// <param name="log">Where to say what was written, which is what an operator needs to find it.</param>
    public LocalDiskAttachmentStorage(string root, ILogger<LocalDiskAttachmentStorage> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.GetFullPath(root);
        _log = log;

        // At construction rather than at the first write, so a host configured with a path it
        // cannot create fails at startup instead of on the first technician's upload.
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(StorageKey key, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = PathFor(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Truncating rather than appending: the only thing that can arrive twice under one key is
        // the same capture retried, and half of an interrupted upload followed by a whole one must
        // leave the whole one.
        await using (var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true))
        {
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        AttachmentStorageLog.Stored(_log, key.Value);
    }

    public Task<Stream?> OpenAsync(StorageKey key, CancellationToken ct)
    {
        var path = PathFor(key);

        if (!File.Exists(path))
        {
            // A metadata row whose blob is missing is a state a caller can report on — somebody
            // cleared the directory, or the volume is not mounted — and a 500 would say less.
            return Task.FromResult<Stream?>(null);
        }

        Stream content = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);

        return Task.FromResult<Stream?>(content);
    }

    private string PathFor(StorageKey key)
    {
        // The key is two uuids and a slash by construction, so this cannot escape the root today.
        // It is checked anyway: the check costs nothing, and the thing it prevents is reading
        // arbitrary files off the host if the key type ever loosens.
        var path = Path.GetFullPath(Path.Combine(_root, key.Value));

        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Storage key '{key.Value}' resolves outside the attachment store.");
        }

        return path;
    }
}

/// <summary>
/// What the adapter has to say, as a source-generated log method.
/// </summary>
/// <remarks>
/// Generated rather than a <c>LogDebug</c> call, for the reason <c>PipelineLog</c> gives: the level
/// is tested before the argument is touched, and the template is checked at compile time.
/// </remarks>
internal static partial class AttachmentStorageLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored attachment content at {StorageKey}")]
    internal static partial void Stored(ILogger logger, string storageKey);
}
