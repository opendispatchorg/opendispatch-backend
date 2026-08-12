using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Invoicing.GenerateInvoice;
using OpenDispatch.Application.Invoicing.MarkPaid;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Api.Invoicing;

/// <summary>
/// The end of the call-to-cash loop over HTTP: raise a bill for a finished job, then settle it
/// (Document 3, step 49).
/// </summary>
/// <remarks>
/// <c>AdminOnly</c>, unlike Customers/Jobs/Schedule/Dispatch's <c>AdminOrDispatcher</c>. Money is
/// the one office-side concern Document 1's persona split does not give the dispatcher: "books,
/// schedules, dispatches" is the operational day, and billing sits with "runs everything" instead
/// — the same reasoning that put Technicians' writes on the admin side at step 47. Not a route
/// under <c>/jobs</c> and <c>/invoices</c> sharing one policy so much as one feature exposed at the
/// two URLs the build text names.
/// </remarks>
public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/jobs/{id:guid}/invoice", InvoiceAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithTags("Invoicing")
            .WithName("GenerateInvoice");

        endpoints.MapPost("/invoices/{id:guid}/pay", PayAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithTags("Invoicing")
            .WithName("PayInvoice");

        return endpoints;
    }

    private static async Task<IResult> InvoiceAsync(
        Guid id,
        CreateInvoiceRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines
            .Select(line => new InvoiceLine(
                (Domain.Invoices.LineItemKind)line.Kind, line.Description, line.Quantity, line.UnitPrice))
            .ToArray();
        var command = new GenerateInvoiceCommand(JobId.From(id), lines);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(
            invoice => Results.Created($"/invoices/{invoice.Id.Value}", ToResponse(invoice)));
    }

    private static async Task<IResult> PayAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new MarkPaidCommand(InvoiceId.From(id)), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult();
    }

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
}
