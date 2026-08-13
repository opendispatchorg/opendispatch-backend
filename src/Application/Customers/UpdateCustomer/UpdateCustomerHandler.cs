using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Customers;

namespace OpenDispatch.Application.Customers.UpdateCustomer;

/// <summary>Loads the customer and corrects their record.</summary>
internal sealed class UpdateCustomerHandler(ICustomerRepository customers)
    : IRequestHandler<UpdateCustomerCommand, Result>
{
    public async Task<Result> Handle(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure(CustomerErrors.NotFound(command.Id));
        }

        customer.Rename(command.Name);
        customer.SetContact(new ContactInfo(command.Email, command.Phone));

        return Result.Success();
    }
}
