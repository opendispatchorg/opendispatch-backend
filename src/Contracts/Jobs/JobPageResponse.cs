namespace OpenDispatch.Contracts.Jobs;

/// <summary>
/// One page of jobs. The body of <c>GET /jobs?page={n}&amp;pageSize={n}</c>.
/// </summary>
/// <remarks>
/// The same shape and the same reasoning as <c>CustomerPageResponse</c> — and the more urgent of
/// the two, because a job list grows every working day rather than with the customer book.
/// </remarks>
/// <param name="Items">The jobs on this page, soonest-promised first.</param>
/// <param name="Total">How many the tenant has altogether.</param>
/// <param name="Page">Which page this is, counting from one.</param>
/// <param name="PageSize">How many this page was allowed to hold.</param>
public sealed record JobPageResponse(
    IReadOnlyList<JobResponse> Items,
    int Total,
    int Page,
    int PageSize);
