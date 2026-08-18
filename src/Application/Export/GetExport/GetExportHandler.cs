using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Application.Export.GetExport;

/// <summary>
/// Reads every repository in the tenant and hands back what each already knows how to project.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It reads nothing.</strong> Each port hands back a stream, the handler projects it, and
/// the rows are pulled by whatever writes the response — so a tenant's whole history never exists
/// in memory, which is what the materialized version could not promise as a shop got older.
/// </para>
/// <para>
/// The five streams must still be drained one after another, not at once: they share the one
/// scoped <c>DbContext</c> the request owns, and EF Core refuses a second operation started before
/// the first has finished. That was this handler's rule when it awaited five reads in sequence and
/// it is now the writer's — <c>ExportEndpoints</c> writes the arrays in order for exactly this
/// reason.
/// </para>
/// <para>
/// A query, so no transaction and nothing here can change anything — the same guarantee
/// <c>GetCustomerHandler</c> relies on to project immediately and trust the projection stays true.
/// </para>
/// </remarks>
internal sealed class GetExportHandler(
    ITechnicianRepository technicians,
    ICustomerRepository customers,
    IJobRepository jobs,
    IAssignmentRepository assignments,
    IInvoiceRepository invoices,
    IAttachmentRepository attachments)
    : IRequestHandler<GetExportQuery, Result<TenantExport>>
{
    public Task<Result<TenantExport>> Handle(GetExportQuery query, CancellationToken cancellationToken)
    {
        // Nothing is read here. Five streams are described and handed back, and the rows arrive
        // when the edge writes them — which is what keeps a whole business out of memory. The
        // sequencing rule the old version obeyed still holds and is now the writer's: one context,
        // one operation at a time, so the streams are drained one after another rather than at once.
        return Task.FromResult(Result.Success(new TenantExport(
            technicians.StreamAsync(cancellationToken).Select(ProjectTechnician),
            customers.StreamAsync(cancellationToken).Select(ProjectCustomer),
            jobs.StreamAsync(cancellationToken).Select(ProjectJob),
            assignments.StreamAsync(cancellationToken).Select(ProjectAssignment),
            invoices.StreamAsync(cancellationToken).Select(ProjectInvoice),
            attachments.StreamAsync(cancellationToken).Select(ProjectAttachment))));
    }

    /// <remarks>
    /// The same projection <c>GET /technicians</c> answers with, sorted skills and all: an export is
    /// the shop's own records in the shapes the rest of the API already speaks.
    /// </remarks>
    private static TechnicianSummary ProjectTechnician(Technician technician) => new(
        technician.Id,
        technician.Name,
        [.. technician.Skills.Order(StringComparer.OrdinalIgnoreCase)],
        technician.Shift.Start,
        technician.Shift.End,
        technician.HomeBase.Lat,
        technician.HomeBase.Lng,
        technician.RetiredAt);

    private static CustomerDetail ProjectCustomer(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Contact.Email,
        customer.Contact.Phone,
        [.. customer.Locations.Select(location => new ServiceLocationDetail(
            location.Id,
            location.Label,
            location.Address,
            location.Point.Lat,
            location.Point.Lng))],
        customer.ErasedAt,
        customer.RetiredAt);

    private static JobSummary ProjectJob(Job job) => new(
        job.Id,
        job.CustomerId,
        job.LocationId,
        job.Location.Lat,
        job.Location.Lng,
        job.RequiredSkill,
        job.Priority,
        job.Window.Start,
        job.Window.End,
        job.EstimatedDuration,
        job.Status,
        job.Notes,
        job.ErasedAt,
        JobLineProjection.Of(job));

    private static AssignmentSummary ProjectAssignment(Assignment assignment) => new(
        assignment.Id,
        assignment.JobId,
        assignment.TechnicianId,
        assignment.Sequence,
        assignment.ScheduledStart,
        assignment.TravelMin);

    private static InvoiceSummary ProjectInvoice(Invoice invoice) => new(
        invoice.Id,
        invoice.JobId,
        invoice.Status,
        invoice.Issued,
        [.. invoice.Lines.Select(line => new InvoiceLineSummary(
            line.Kind, line.Description, line.Quantity, line.UnitPrice, line.LineTotal))],
        invoice.Total);

    private static AttachmentSummary ProjectAttachment(Attachment attachment) => new(
        attachment.Id,
        attachment.JobId,
        attachment.Kind,
        attachment.StorageKey.Value,
        attachment.CreatedAt);
}
