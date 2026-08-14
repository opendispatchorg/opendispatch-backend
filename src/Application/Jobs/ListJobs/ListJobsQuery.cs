using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Jobs.ListJobs;

/// <summary>One page of this tenant's jobs, soonest-promised first.</summary>
/// <param name="Page">Which page, counting from one.</param>
/// <param name="PageSize">How many jobs it may hold, up to <see cref="MaxPageSize"/>.</param>
/// <remarks>
/// Paged for the reason <c>ListCustomersQuery</c> is, and more urgently: a job list grows every
/// working day of the business's life, so this was the query most certain to end up handing a
/// dispatcher a year of history to draw one screen.
/// </remarks>
public sealed record ListJobsQuery(int Page = 1, int PageSize = ListJobsQuery.DefaultPageSize)
    : IQuery<JobPage>
{
    /// <summary>What a caller gets when it does not ask.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page this endpoint will serve — see <c>ListCustomersQuery.MaxPageSize</c>.</summary>
    public const int MaxPageSize = 200;
}

/// <summary>
/// One page of jobs, and how many there are to page through.
/// </summary>
/// <param name="Items">The jobs on this page.</param>
/// <param name="Total">How many the tenant has altogether.</param>
/// <param name="Page">Which page this is.</param>
/// <param name="PageSize">How many it was allowed to hold.</param>
public sealed record JobPage(IReadOnlyList<JobSummary> Items, int Total, int Page, int PageSize);
