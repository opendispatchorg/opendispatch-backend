using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Customers.ListCustomers;
using OpenDispatch.Application.Customers.RemoveServiceLocation;
using OpenDispatch.Application.Customers.UpdateCustomer;
using OpenDispatch.Application.Customers.UpdateServiceLocation;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.Customers;

/// <summary>
/// Customers and the service locations they own — CRUD over the slices steps 32 and 47 built
/// (Document 3, step 47).
/// </summary>
/// <remarks>
/// Every route here is <see cref="AuthPolicies.AdminOrDispatcher"/>: this is the office-side
/// surface a dispatcher uses to take on a customer or look one up before booking a job, and an
/// admin uses for the same reasons. A technician reaches customer data, if at all, through the
/// sync endpoints (step 50) — never through here.
/// </remarks>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customers = endpoints.MapGroup("/customers")
            .RequireAuthorization(AuthPolicies.AdminOrDispatcher)
            .WithTags("Customers");

        customers.MapPost("/", CreateAsync).WithName("CreateCustomer");
        customers.MapGet("/", ListAsync).WithName("ListCustomers");
        customers.MapGet("/{id:guid}", GetAsync).WithName("GetCustomer");
        customers.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateCustomer");

        customers.MapPost("/{id:guid}/locations", AddLocationAsync).WithName("AddServiceLocation");
        customers.MapPut("/{id:guid}/locations/{locationId:guid}", UpdateLocationAsync)
            .WithName("UpdateServiceLocation");
        customers.MapDelete("/{id:guid}/locations/{locationId:guid}", RemoveLocationAsync)
            .WithName("RemoveServiceLocation");

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateCustomerRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new CreateCustomerCommand(request.Name, request.Email, request.Phone), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(id => Results.Created(
            $"/customers/{id.Value}",
            new CustomerSummaryResponse(id.Value, request.Name, request.Email, request.Phone)));
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListCustomersQuery(), cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(customers => Results.Ok(customers.Select(ToSummary)));
    }

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new GetCustomerQuery(CustomerId.From(id)), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(customer => Results.Ok(ToResponse(customer)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateCustomerRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCustomerCommand(CustomerId.From(id), request.Name, request.Email, request.Phone);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static async Task<IResult> AddLocationAsync(
        Guid id,
        ServiceLocationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new AddServiceLocationCommand(
            CustomerId.From(id), request.Label, request.Address, request.Latitude, request.Longitude);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(locationId => Results.Created(
            $"/customers/{id}/locations/{locationId.Value}",
            new ServiceLocationResponse(locationId.Value, request.Label, request.Address, request.Latitude, request.Longitude)));
    }

    private static async Task<IResult> UpdateLocationAsync(
        Guid id,
        Guid locationId,
        ServiceLocationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateServiceLocationCommand(
            CustomerId.From(id),
            ServiceLocationId.From(locationId),
            request.Label,
            request.Address,
            request.Latitude,
            request.Longitude);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static async Task<IResult> RemoveLocationAsync(
        Guid id,
        Guid locationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new RemoveServiceLocationCommand(CustomerId.From(id), ServiceLocationId.From(locationId));
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static CustomerSummaryResponse ToSummary(CustomerSummary customer) =>
        new(customer.Id.Value, customer.Name, customer.Email, customer.Phone);

    private static CustomerResponse ToResponse(CustomerDetail customer) => new(
        customer.Id.Value,
        customer.Name,
        customer.Email,
        customer.Phone,
        [.. customer.Locations.Select(location =>
            new ServiceLocationResponse(location.Id.Value, location.Label, location.Address, location.Latitude, location.Longitude))]);
}
