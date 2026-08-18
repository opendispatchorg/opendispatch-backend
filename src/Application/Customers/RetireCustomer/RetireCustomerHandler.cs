using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Customers.RetireCustomer;

/// <summary>
/// Retires a customer, or puts them back on the books.
/// </summary>
/// <remarks>
/// The aggregate owns both the flag and the refusal to touch an erased record, so this decides
/// nothing beyond which customer and which direction. Retiring one already retired is a no-op, so
/// a repeated click is harmless rather than an error somebody has to interpret.
/// </remarks>
internal sealed class RetireCustomerHandler(ICustomerRepository customers, IClock clock)
    : IRequestHandler<RetireCustomerCommand, Result>
{
    public async Task<Result> Handle(RetireCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure(CustomerErrors.NotFound(command.Id));
        }

        // An erased customer refuses both directions, from inside the aggregate — reinstating one
        // would be editing somebody back into existence after a promise that they were gone.
        if (customer.IsErased)
        {
            return Result.Failure(CustomerErrors.Erased(command.Id));
        }

        if (command.Retired)
        {
            customer.Retire(clock.UtcNow);
        }
        else
        {
            customer.Reinstate();
        }

        return Result.Success();
    }
}
