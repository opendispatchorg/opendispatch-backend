using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Jobs;

namespace OpenDispatch.Application.Export;

/// <summary>
/// The whole of a tenant's business data, as Document 1's anti-lock-in promise names it: "your
/// customers, your jobs, your data — on software you control."
/// </summary>
/// <param name="Customers">Every customer, with their service locations.</param>
/// <param name="Jobs">Every job, whatever its status.</param>
/// <param name="Assignments">The plan: every stop, whichever technician it is on.</param>
/// <param name="Invoices">Every bill raised, with its lines and whether it is settled.</param>
/// <param name="Attachments">Every photo and signature captured, by metadata — not their bytes.</param>
/// <remarks>
/// <para>
/// <strong>Five streams, not five lists, and the difference is what makes this endpoint safe to
/// offer.</strong> A shop's whole history was being read into memory and then serialized into a
/// second copy of itself before a byte reached the client — twice the size of the export, held at
/// once, for as long as the response took. Nothing here is materialized: the rows arrive from the
/// database as the JSON writer asks for them, so the memory cost is one row rather than one
/// business.
/// </para>
/// <para>
/// The cost of that is where a failure lands. A stream that faults halfway leaves a truncated
/// response with a 200 already on it, because the status line went out before the first row was
/// read. That is the ordinary trade for streaming anything, and the honest answer for a client is
/// the same as for any download: a body that does not parse is a failed export, and the request is
/// repeatable.
/// </para>
/// <para>
/// Built entirely from shapes the rest of the application already had a reason to define —
/// <see cref="CustomerDetail"/> is what <c>GET /customers/{id}</c> already returns,
/// <see cref="JobSummary"/> is what <c>GET /jobs</c> already returns, <see cref="InvoiceSummary"/>
/// is what raising an invoice already returns. <see cref="AssignmentSummary"/> and
/// <see cref="AttachmentSummary"/> are the two exceptions, because nothing before step 49 ever
/// wanted the plan on its own rather than joined into a board, and nothing before step 50b ever
/// read attachment metadata back out at all.
/// </para>
/// <para>
/// Attachments carry a server id, not their content — the same reasoning
/// <see cref="AttachmentSummary"/> gives: a full-tenant dump the size of every photograph a shop
/// has ever taken would turn "export my data" into a multi-gigabyte download nobody asked for. A
/// device or an operator that wants the bytes behind an entry here already has what it needs to ask
/// for them by the same means an upload uses to write them.
/// </para>
/// <para>
/// Technicians are still not a list here — the build text names customers, jobs, assignments and
/// invoices, step 50b adds attachments to that by name, and a crew roster remains staffing data
/// rather than the call-to-cash record this endpoint hands back whole.
/// </para>
/// </remarks>
public sealed record TenantExport(
    IAsyncEnumerable<CustomerDetail> Customers,
    IAsyncEnumerable<JobSummary> Jobs,
    IAsyncEnumerable<AssignmentSummary> Assignments,
    IAsyncEnumerable<InvoiceSummary> Invoices,
    IAsyncEnumerable<AttachmentSummary> Attachments);
