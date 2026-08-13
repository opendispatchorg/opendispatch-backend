using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Export.GetExport;

/// <summary>
/// Reads every repository in the tenant and hands back what each already knows how to project.
/// </summary>
/// <remarks>
/// <para>
/// The five reads run one after another, not in parallel: they share the one scoped
/// <c>DbContext</c> the request's unit of work owns, and EF Core refuses a second operation
/// started on a context before the first has finished. The same reason every other multi-read
/// handler in this codebase (<c>OptimizeDayHandler</c>, <c>InsertJobHandler</c>) awaits its reads
/// in sequence.
/// </para>
/// <para>
/// A query, so no transaction and nothing here can change anything — the same guarantee
/// <c>GetCustomerHandler</c> relies on to project immediately and trust the projection stays true.
/// </para>
/// </remarks>
internal sealed class GetExportHandler(
    ICustomerRepository customers,
    IJobRepository jobs,
    IAssignmentRepository assignments,
    IInvoiceRepository invoices,
    IAttachmentRepository attachments)
    : IRequestHandler<GetExportQuery, Result<TenantExport>>
{
    public async Task<Result<TenantExport>> Handle(GetExportQuery query, CancellationToken cancellationToken)
    {
        var foundCustomers = await customers.ListAsync(cancellationToken).ConfigureAwait(false);
        var foundJobs = await jobs.ListAsync(cancellationToken).ConfigureAwait(false);
        var foundAssignments = await assignments.ListAsync(cancellationToken).ConfigureAwait(false);
        var foundInvoices = await invoices.ListAsync(cancellationToken).ConfigureAwait(false);
        var foundAttachments = await attachments.ListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new TenantExport(
            [.. foundCustomers.Select(ProjectCustomer)],
            [.. foundJobs.Select(ProjectJob)],
            [.. foundAssignments.Select(ProjectAssignment)],
            [.. foundInvoices.Select(ProjectInvoice)],
            [.. foundAttachments.Select(ProjectAttachment)]));
    }

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
            location.Point.Lng))]);

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
        job.Notes);

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
