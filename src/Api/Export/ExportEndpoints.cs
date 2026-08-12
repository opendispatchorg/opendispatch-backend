using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Export;
using OpenDispatch.Application.Export.GetExport;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Export;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Api.Export;

/// <summary>
/// <c>GET /export</c> — Document 1's anti-lock-in feature over HTTP (Document 3, steps 49 and
/// 50b): every customer, job, assignment, invoice and attachment's metadata in the tenant, in one
/// JSON dump.
/// </summary>
/// <remarks>
/// <c>AdminOnly</c>, the same call as Invoicing beside it: a full-tenant data dump is a business
/// ownership question, not a day-to-day dispatching one, and Document 1's persona split puts "runs
/// everything" — which includes the data itself — on the admin side.
/// </remarks>
public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/export", ExportAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithTags("Export")
            .WithName("ExportTenant");

        return endpoints;
    }

    private static async Task<IResult> ExportAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetExportQuery(), cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(export => Results.Ok(ToResponse(export)));
    }

    private static ExportResponse ToResponse(TenantExport export) => new(
        [.. export.Customers.Select(ToResponse)],
        [.. export.Jobs.Select(ToResponse)],
        [.. export.Assignments.Select(ToResponse)],
        [.. export.Invoices.Select(ToResponse)],
        [.. export.Attachments.Select(ToResponse)]);

    private static CustomerResponse ToResponse(CustomerDetail customer) => new(
        customer.Id.Value,
        customer.Name,
        customer.Email,
        customer.Phone,
        [.. customer.Locations.Select(location =>
            new ServiceLocationResponse(location.Id.Value, location.Label, location.Address, location.Latitude, location.Longitude))]);

    private static JobResponse ToResponse(JobSummary job) => new(
        job.Id.Value,
        job.CustomerId.Value,
        job.LocationId.Value,
        job.Latitude,
        job.Longitude,
        job.RequiredSkill,
        (Contracts.JobPriority)job.Priority,
        job.WindowStart,
        job.WindowEnd,
        job.EstimatedDuration,
        (Contracts.JobStatus)job.Status,
        job.Notes);

    private static AssignmentExport ToResponse(AssignmentSummary assignment) => new(
        assignment.Id.Value,
        assignment.JobId.Value,
        assignment.TechnicianId.Value,
        assignment.Sequence,
        assignment.ScheduledStart,
        assignment.TravelMin);

    private static InvoiceResponse ToResponse(InvoiceSummary invoice) => new(
        invoice.Id.Value,
        invoice.JobId.Value,
        (Contracts.InvoiceStatus)invoice.Status,
        invoice.Issued,
        [.. invoice.Lines.Select(ToResponse)],
        ToDollars(invoice.Total));

    private static InvoiceLineResponse ToResponse(InvoiceLineSummary line) => new(
        (Contracts.LineItemKind)line.Kind,
        line.Description,
        line.Quantity,
        ToDollars(line.UnitPrice),
        ToDollars(line.LineTotal));

    private static decimal ToDollars(Money money) => money.Cents / 100m;

    private static AttachmentExport ToResponse(AttachmentSummary attachment) => new(
        attachment.Id.Value,
        attachment.JobId.Value,
        (Contracts.AttachmentKind)attachment.Kind,
        attachment.ServerId,
        attachment.CreatedAt);
}
