using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Jobs.ListJobs;

/// <summary>Every job on this tenant's books, soonest-promised first.</summary>
/// <remarks>
/// No parameters and therefore no validator, as with <c>ListCustomersQuery</c>. Unpaged too,
/// following <c>IJobRepository.ListAsync</c> — the same "will not age well" note applies here as
/// it does to the customer list.
/// </remarks>
public sealed record ListJobsQuery : IQuery<IReadOnlyList<JobSummary>>;
