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
/// <remarks>
/// <para>
/// Built entirely from shapes the rest of the application already had a reason to define —
/// <see cref="CustomerDetail"/> is what <c>GET /customers/{id}</c> already returns,
/// <see cref="JobSummary"/> is what <c>GET /jobs</c> already returns, <see cref="InvoiceSummary"/>
/// is what raising an invoice already returns. Only <see cref="AssignmentSummary"/> is new,
/// because nothing before this query has ever wanted the plan on its own rather than joined into a
/// board or scoped to a horizon.
/// </para>
/// <para>
/// Technicians are deliberately not a fifth list here — the build text names customers, jobs,
/// assignments and invoices and no more, and a crew roster is staffing data rather than the
/// call-to-cash record this endpoint promises to hand back whole.
/// </para>
/// </remarks>
public sealed record TenantExport(
    IReadOnlyList<CustomerDetail> Customers,
    IReadOnlyList<JobSummary> Jobs,
    IReadOnlyList<AssignmentSummary> Assignments,
    IReadOnlyList<InvoiceSummary> Invoices);
