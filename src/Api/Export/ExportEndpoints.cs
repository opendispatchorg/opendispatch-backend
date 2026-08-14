using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
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
            .WithName("ExportTenant")
            .Produces<ExportResponse>();

        return endpoints;
    }

    private static async Task<IResult> ExportAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetExportQuery(), cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(export => new StreamedExport(export));
    }

    /// <summary>
    /// Writes the export as it is read, one row at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Written by hand rather than serialized, because there is nothing to serialize.</strong>
    /// The handler hands back five streams (see <c>TenantExport</c>); materializing them into an
    /// <c>ExportResponse</c> to hand to <c>Results.Ok</c> would put a shop's whole history in memory
    /// twice over, which is the thing this change exists to stop.
    /// </para>
    /// <para>
    /// The <em>shape</em> is unchanged, and that is deliberate: the same property names in the same
    /// order as <c>ExportResponse</c>, so the OpenAPI document, the generated TypeScript and every
    /// client stay exactly as they were. <c>Produces&lt;ExportResponse&gt;</c> on the route is what
    /// keeps the document honest, and <c>ExportEndpointsFlowTests</c> reads the body back through
    /// that type — if this writer and that record ever disagree, the test stops parsing.
    /// </para>
    /// <para>
    /// The five arrays are written one after another because they share one <c>DbContext</c>, which
    /// permits one operation at a time. The writer is flushed between them so a large export
    /// travels rather than accumulating in a buffer.
    /// </para>
    /// </remarks>
    private sealed class StreamedExport(TenantExport export) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var options = httpContext.RequestServices
                .GetRequiredService<IOptions<JsonOptions>>()
                .Value.SerializerOptions;

            httpContext.Response.ContentType = "application/json; charset=utf-8";

            var cancellationToken = httpContext.RequestAborted;

            await using var writer = new Utf8JsonWriter(httpContext.Response.BodyWriter);

            writer.WriteStartObject();

            await WriteAsync(writer, options, "customers", export.Customers, ToResponse, cancellationToken)
                .ConfigureAwait(false);
            await WriteAsync(writer, options, "jobs", export.Jobs, ToResponse, cancellationToken)
                .ConfigureAwait(false);
            await WriteAsync(writer, options, "assignments", export.Assignments, ToResponse, cancellationToken)
                .ConfigureAwait(false);
            await WriteAsync(writer, options, "invoices", export.Invoices, ToResponse, cancellationToken)
                .ConfigureAwait(false);
            await WriteAsync(writer, options, "attachments", export.Attachments, ToResponse, cancellationToken)
                .ConfigureAwait(false);

            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <remarks>
        /// Each row is serialized with the host's own <c>JsonSerializerOptions</c> — the same
        /// naming policy, the same converters — so a streamed field is byte-for-byte what
        /// <c>Results.Ok</c> would have written. Only the array framing is this method's.
        /// </remarks>
        private static async Task WriteAsync<TSource, TResponse>(
            Utf8JsonWriter writer,
            JsonSerializerOptions options,
            string name,
            IAsyncEnumerable<TSource> rows,
            Func<TSource, TResponse> project,
            CancellationToken cancellationToken)
        {
            writer.WriteStartArray(name);

            await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                JsonSerializer.Serialize(writer, project(row), options);

                // Flushed row by row rather than at the end: the whole point is that neither this
                // process nor the client waits for a business's history to be assembled.
                if (writer.BytesPending > FlushThreshold)
                {
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            writer.WriteEndArray();
        }

        /// <summary>How much is allowed to accumulate before it is pushed to the client.</summary>
        /// <remarks>
        /// Sixteen kilobytes: large enough that a small export is one write, small enough that a
        /// large one is never held. Flushing every row would be a syscall per customer.
        /// </remarks>
        private const int FlushThreshold = 16 * 1024;
    }

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
