using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Pipeline;

/// <summary>
/// A sample command that writes a customer and then either stands by it or refuses.
/// </summary>
/// <remarks>
/// It writes through the real ports so the transaction under test is a real one. The first
/// slice to do this for real is step 32; until then a sample is what lets the pipeline be proved
/// against a database without inventing a feature ahead of its step.
/// </remarks>
/// <param name="Name">What to call the customer.</param>
/// <param name="ThenRefuse">Whether to report a failure after writing.</param>
internal sealed record TakeOnCustomerCommand(string Name, bool ThenRefuse) : ICommand<CustomerId>;

/// <summary>Writes, saves, and then decides.</summary>
internal sealed class TakeOnCustomerHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ITenantContext tenant)
    : IRequestHandler<TakeOnCustomerCommand, Result<CustomerId>>
{
    /// <summary>The failure the handler reports when told to refuse.</summary>
    public static Error Refused { get; } =
        Error.Conflict("sample.refused", "The sample handler refused after writing.");

    public async Task<Result<CustomerId>> Handle(
        TakeOnCustomerCommand command,
        CancellationToken cancellationToken)
    {
        // The tenant comes from the ambient context and never from the command — the rule the
        // query filters cannot enforce, since they constrain reads and not writes.
        var customer = CustomerBuilder.Any().ForOrg(tenant.OrgId).Named(command.Name).Build();
        customers.Add(customer);

        // Saved here rather than left to the pipeline, deliberately: the row has to have been
        // written for "it is not there afterwards" to mean the transaction took it back. A test
        // over a handler that only staged would pass against a pipeline that simply forgot to
        // save.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return command.ThenRefuse
            ? Result.Failure<CustomerId>(Refused)
            : Result.Success(customer.Id);
    }
}
