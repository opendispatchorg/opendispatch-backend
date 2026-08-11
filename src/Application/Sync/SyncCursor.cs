using System.Globalization;

namespace OpenDispatch.Application.Sync;

/// <summary>
/// Where a device stands in the server's stream of changes: a watermark it sends back to be told
/// everything that has happened since.
/// </summary>
/// <remarks>
/// <para>
/// Opaque to clients (the wire type is a string, see <c>SyncPullResponse.Cursor</c>) and
/// deliberately so — it is a position in a change stream, not a timestamp or a count of anything,
/// and a client doing arithmetic on it would be relying on something it was never promised.
/// </para>
/// <para>
/// The one property it does promise is that it only moves forward, so a device can compare two of
/// its own cursors and keep the later. What produces the values, and why "changed since" is
/// inclusive of the cursor rather than exclusive, is <c>ISyncCursorSource</c>'s business.
/// </para>
/// </remarks>
/// <param name="Value">The watermark. Meaningful only to the server that issued it.</param>
public readonly record struct SyncCursor(long Value)
{
    /// <summary>
    /// The cursor of a device that has never synced: everything the server holds is news to it.
    /// </summary>
    public static SyncCursor Beginning => new(0);

    /// <summary>Reads a cursor a device sent back.</summary>
    /// <param name="text">The cursor as it was handed out.</param>
    /// <param name="cursor">The parsed cursor, or <see cref="Beginning"/> when it is not one.</param>
    /// <returns>
    /// <see langword="true"/> if the text is a cursor this server issued the shape of. A client
    /// that invents one is refused rather than quietly served from the beginning of time, which
    /// would turn a typo into a full resync of the tenant.
    /// </returns>
    public static bool TryParse(string? text, out SyncCursor cursor)
    {
        cursor = Beginning;

        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        cursor = new SyncCursor(value);

        return true;
    }

    /// <summary>The form a device is given and sends back.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
