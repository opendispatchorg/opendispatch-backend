using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.CreateCustomer;

/// <summary>
/// Creates the customer and stages it. The pipeline decides whether it is kept.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>IUnitOfWork</c> here even though the step names it: <c>TransactionBehavior</c>
/// saves and commits every successful command, so a handler that saved as well would be a second
/// way to say the same thing — and one that a later handler could get half right. A handler saves
/// only when it needs a write <em>ordered</em> before something else it does, which this one does
/// not: the id comes from <c>Customer.Create</c>, not from the database.
/// </para>
/// <para>
/// Nothing here checks that the name is non-blank. <c>Customer.Create</c> does, because it is a
/// rule about what a customer is; the validator refuses it first so the caller is told which
/// field was wrong rather than being handed a <c>DomainException</c>. Both are meant to be there.
/// </para>
/// </remarks>
internal sealed class CreateCustomerHandler(ICustomerRepository customers, ITenantContext tenant)
    : IRequestHandler<CreateCustomerCommand, Result<CustomerId>>
{
    public Task<Result<CustomerId>> Handle(
        CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var customer = Customer.Create(
            tenant.OrgId,
            command.Name,
            new ContactInfo(command.Email, command.Phone));

        customers.Add(customer);

        return Task.FromResult(Result.Success(customer.Id));
    }
}
