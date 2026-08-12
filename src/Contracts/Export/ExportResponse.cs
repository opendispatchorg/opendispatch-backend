using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Contracts.Jobs;

namespace OpenDispatch.Contracts.Export;

/// <summary>
/// The whole of a tenant's business data — the response from <c>GET /export</c>, Document 1's
/// anti-lock-in feature: "your customers, your jobs, your data — on software you control."
/// </summary>
/// <param name="Customers">Every customer, with their service locations.</param>
/// <param name="Jobs">Every job, whatever its status.</param>
/// <param name="Assignments">The plan: every stop, whichever technician it is on.</param>
/// <param name="Invoices">Every bill raised, with its lines and whether it is settled.</param>
/// <remarks>
/// Built entirely from response shapes that already exist for their own endpoints —
/// <see cref="CustomerResponse"/>, <see cref="JobResponse"/> and <see cref="InvoiceResponse"/> —
/// rather than a parallel set invented for this one. A client that already knows how to read a
/// customer or a job from the rest of the API reads one here the same way. Only
/// <see cref="AssignmentExport"/> is new: nothing before this endpoint has ever exposed the plan on
/// its own, flat, outside a technician's lane on the dispatch board.
/// </remarks>
public sealed record ExportResponse(
    IReadOnlyList<CustomerResponse> Customers,
    IReadOnlyList<JobResponse> Jobs,
    IReadOnlyList<AssignmentExport> Assignments,
    IReadOnlyList<InvoiceResponse> Invoices);
