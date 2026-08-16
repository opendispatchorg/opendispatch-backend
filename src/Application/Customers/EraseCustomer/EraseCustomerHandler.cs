using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Customers.EraseCustomer;

/// <summary>
/// Erases the customer, their jobs, and the photographs taken on those jobs.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The order is the interesting part.</strong> The bytes are deleted before the transaction
/// commits, not after. Both orders can fail badly, and they fail differently: deleting after the
/// commit means a process that dies in between leaves the photographs on the disk with nothing left
/// to say they should have gone, which is the failure an erasure must not have. Deleting first means
/// a failed commit can leave metadata rows pointing at blobs that are gone — visible, reportable
/// (<c>IAttachmentStorage.OpenAsync</c> answers null), and fixed by running the erasure again, which
/// is exactly what an operator does when a request fails.
/// </para>
/// <para>
/// The domain does the erasing; this only decides what is in scope. Which fields go and which stay
/// is an invariant that lives on <c>Customer</c> and <c>Job</c>, so a second caller cannot erase a
/// person differently.
/// </para>
/// </remarks>
internal sealed class EraseCustomerHandler(
    ICustomerRepository customers,
    IJobRepository jobs,
    IAttachmentRepository attachments,
    IAttachmentStorage storage,
    IClock clock)
    : IRequestHandler<EraseCustomerCommand, Result>
{
    public async Task<Result> Handle(EraseCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure(CustomerErrors.NotFound(command.Id));
        }

        var at = clock.UtcNow;

        customer.Erase(at);

        var theirJobs = await jobs.ListForCustomerAsync(command.Id, cancellationToken).ConfigureAwait(false);

        foreach (var job in theirJobs)
        {
            job.Erase(at);

            var captured = await attachments.ListForJobAsync(job.Id, cancellationToken).ConfigureAwait(false);

            foreach (var attachment in captured)
            {
                await storage.DeleteAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);
                attachments.Remove(attachment);
            }
        }

        return Result.Success();
    }
}
