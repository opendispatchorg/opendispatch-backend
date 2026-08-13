using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Export.GetExport;

/// <summary>The whole tenant, whole — <c>GET /export</c> (Document 3, step 49).</summary>
/// <remarks>
/// No parameters and therefore no validator, as <c>ListCustomersQuery</c> and <c>ListJobsQuery</c>
/// already establish for an unpaged "everything" read.
/// </remarks>
public sealed record GetExportQuery : IQuery<TenantExport>;
