using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Technicians;

namespace OpenDispatch.Contracts.Export;

/// <summary>
/// The whole of a tenant's business data — the response from <c>GET /export</c>, Document 1's
/// anti-lock-in feature: "your customers, your jobs, your data — on software you control."
/// </summary>
/// <param name="Technicians">The crew: who works here, what they hold, and when they work.</param>
/// <param name="Customers">Every customer, with their service locations.</param>
/// <param name="Jobs">Every job, whatever its status.</param>
/// <param name="Assignments">The plan: every stop, whichever technician it is on.</param>
/// <param name="Invoices">Every bill raised, with its lines and whether it is settled.</param>
/// <param name="Attachments">Every photo and signature captured, by metadata — not their bytes.</param>
/// <remarks>
/// Built entirely from response shapes that already exist for their own endpoints —
/// <see cref="CustomerResponse"/>, <see cref="JobResponse"/> and <see cref="InvoiceResponse"/> —
/// and <see cref="TechnicianResponse"/> — rather than a parallel set invented for this one. A client that already knows how to read a
/// customer or a job from the rest of the API reads one here the same way. <see cref="AssignmentExport"/>
/// and <see cref="AttachmentExport"/> are the two exceptions: nothing before step 49 ever exposed
/// the plan on its own, flat, outside a technician's lane on the dispatch board, and nothing before
/// step 50b ever read attachment metadata back out at all.
/// </remarks>
public sealed record ExportResponse(
    IReadOnlyList<TechnicianResponse> Technicians,
    IReadOnlyList<CustomerResponse> Customers,
    IReadOnlyList<JobResponse> Jobs,
    IReadOnlyList<AssignmentExport> Assignments,
    IReadOnlyList<InvoiceResponse> Invoices,
    IReadOnlyList<AttachmentExport> Attachments);
