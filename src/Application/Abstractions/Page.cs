namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Which slice of a list a caller is asking for.
/// </summary>
/// <param name="Number">Which page, counting from one.</param>
/// <param name="Size">How many rows it may hold.</param>
/// <remarks>
/// <para>
/// One-based, because it is what a caller building "page 3 of 7" already has and what every
/// pagination control in a browser produces. Offsets are the repository's arithmetic, not the
/// caller's.
/// </para>
/// <para>
/// Offset paging rather than a cursor, deliberately, and the difference matters where the sync
/// protocol is concerned: sync uses a cursor because a device must never miss a change, and an
/// offset would skip a row when something is inserted between two pulls. A dispatcher scrolling a
/// customer list has no such requirement — the worst an insert does is show them one name twice —
/// and offsets buy a total and a jump-to-page a cursor cannot.
/// </para>
/// </remarks>
public readonly record struct PageRequest(int Number, int Size)
{
    /// <summary>The rows to skip to reach this page.</summary>
    public int Skip => (Number - 1) * Size;
}

/// <summary>
/// One page of a list, and how much there is to page through.
/// </summary>
/// <typeparam name="TItem">What is being listed.</typeparam>
/// <param name="Items">The rows on this page, in the order the list is sorted.</param>
/// <param name="Total">
/// How many rows there are in the whole list — the second query a page costs, and what a caller
/// needs to know there is a page after this one.
/// </param>
public sealed record Page<TItem>(IReadOnlyList<TItem> Items, int Total);
