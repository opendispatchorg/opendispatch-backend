using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Jobs.GetJob;

/// <summary>Fetches one job.</summary>
/// <param name="Id">Which job.</param>
public sealed record GetJobQuery(JobId Id) : IQuery<JobSummary>;
