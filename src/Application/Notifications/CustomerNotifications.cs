using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Notifications;

/// <summary>
/// Tells a customer the two things a customer wants to be told: somebody is on the way, and the
/// bill is settled.
/// </summary>
/// <remarks>
/// <para>
/// Shaped like <c>BoardNotifications</c> and for the same reasons: one class subscribing several
/// times over, re-reading the aggregates through the ports it is given rather than trusting an
/// event's fields. It is the extension model doing exactly what Document 2 §12 promises — nothing
/// that raises <c>JobEnRoute</c> or <c>InvoicePaid</c> changed to make this work, and nothing knows
/// it exists.
/// </para>
/// <para>
/// <strong>The customer is a second read, not a reference.</strong> A job carries a
/// <see cref="CustomerId"/> and cannot see the customer (aggregates reference each other by id), so
/// reaching an address is a lookup. That is the correct cost of the boundary, and it is one query
/// on a path that is already sending an email over a network.
/// </para>
/// <para>
/// <strong>Nothing here throws.</strong> A domain-event handler runs after the commit but inside the
/// request, and an exception from one both fails that request and stops the handlers behind it — so
/// a mail server being down would turn "the technician is on the way" into a 500 for the dispatcher
/// who pressed the button, and would take the board's own repaint with it. A customer email is not
/// something a dispatch action may fail on. See <c>SendAsync</c> for what that gives up.
/// </para>
/// <para>
/// <strong>Delivery is at least once, so a duplicate is possible.</strong> The outbox guarantees
/// delivery, not uniqueness, and a message the ordinary path published but did not get to forget is
/// published again by the sweep. A second "your technician is on the way" is a mild annoyance; this
/// is exactly why email is the right first channel and why a payment would never be triggered this
/// way.
/// </para>
/// </remarks>
internal sealed class CustomerNotifications(
    IJobRepository jobs,
    ICustomerRepository customers,
    IInvoiceRepository invoices,
    ILogger<CustomerNotifications> log,
    INotificationSender? sender = null)
    : IDomainEventHandler<JobEnRoute>,
      IDomainEventHandler<InvoiceRaised>,
      IDomainEventHandler<InvoicePaid>
{
    public async Task Handle(JobEnRoute domainEvent, CancellationToken cancellationToken)
    {
        if (await AddressFor(domainEvent.JobId, cancellationToken).ConfigureAwait(false) is not { } customer)
        {
            return;
        }

        await SendAsync(
            Notification.Email(
                customer.To,
                "Your technician is on the way",
                $"Hello {customer.Name},\n\n"
                + "Your technician is on the way to you now. They will be with you shortly.\n"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The bill itself — the step between doing the work and getting paid.
    /// </summary>
    /// <remarks>
    /// This is the message a shop actually needs to send, and for a while it could not: the domain
    /// announced an invoice being <em>paid</em> and said nothing when one was raised, so the only
    /// customer email about money was a receipt for a payment nobody had been asked for.
    /// <c>InvoiceRaised</c> closed that, and this is its one subscriber.
    /// </remarks>
    public Task Handle(InvoiceRaised domainEvent, CancellationToken cancellationToken) =>
        BillAsync(
            domainEvent.JobId,
            domainEvent.InvoiceId,
            "Your invoice",
            total => $"Your invoice for the work comes to {total}.\n\n"
                + "Details are on the invoice itself; reply to this message if anything looks wrong.\n",
            cancellationToken);

    /// <remarks>
    /// The receipt, and the other half of the pair: <c>InvoiceRaised</c> asks, this one confirms.
    /// v1 takes no money — see <c>FakePaymentGateway</c> — so what it confirms is that a payment was
    /// <em>recorded</em>, which is what the wording says.
    /// </remarks>
    public Task Handle(InvoicePaid domainEvent, CancellationToken cancellationToken) =>
        BillAsync(
            domainEvent.JobId,
            domainEvent.InvoiceId,
            "Your invoice has been settled",
            total => $"Thank you — we have recorded payment of {total} against your invoice.\n",
            cancellationToken);

    /// <summary>
    /// The shape both invoice messages share: find somebody to tell, read the bill, say the amount.
    /// </summary>
    /// <param name="jobId">The work, which is how the customer is reached.</param>
    /// <param name="invoiceId">The bill, re-read for its current total.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="body">What to say, given the total already formatted.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <remarks>
    /// <strong>The total is re-read rather than carried on the event</strong>, which is what lets
    /// <c>InvoiceRaised</c> be raised by a factory before its caller has added a single line: by the
    /// time a subscriber runs, the transaction has committed and the invoice is whatever it ended up
    /// being. An amount on the event would have been zero for half its raise sites.
    /// </remarks>
    private async Task BillAsync(
        JobId jobId,
        InvoiceId invoiceId,
        string subject,
        Func<string, string> body,
        CancellationToken cancellationToken)
    {
        if (await AddressFor(jobId, cancellationToken).ConfigureAwait(false) is not { } customer)
        {
            return;
        }

        var invoice = await invoices.GetAsync(invoiceId, cancellationToken).ConfigureAwait(false);

        if (invoice is null)
        {
            return;
        }

        await SendAsync(
            Notification.Email(
                customer.To,
                subject,
                $"Hello {customer.Name},\n\n" + body(Amount(invoice.Total))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// An amount as a person reads it.
    /// </summary>
    /// <remarks>
    /// Invariant culture and no currency symbol. <c>Money</c> is a count of cents and this system
    /// does not model which currency they are — writing "$" would be a guess about a shop that may
    /// be in Manchester, and a culture-formatted number would make the same message read differently
    /// depending on which host sent it.
    /// </remarks>
    private static string Amount(Money total) =>
        (total.Cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Where to write to about a job, and who to greet — or <see langword="null"/> if there is
    /// nobody to tell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>An erased customer is never emailed</strong>, and this is the rule that matters most
    /// here. <c>Customer.Erase</c> leaves tombstones and <c>ContactInfo.None</c>, so the address is
    /// already gone — but the erasure is checked outright rather than relied on to have emptied a
    /// field, because the failure mode is writing to whoever holds that address now about work done
    /// for somebody who asked to be forgotten. It is treated as "nobody to tell", not as an error.
    /// </para>
    /// <para>
    /// <strong>No address is not a failure either.</strong> Plenty of a shop's customers have a
    /// phone number and nothing else, and a job booked for one of them must complete exactly as it
    /// does for anybody else.
    /// </para>
    /// </remarks>
    private async Task<(string To, string Name)?> AddressFor(JobId jobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return null;
        }

        var customer = await customers.GetAsync(job.CustomerId, cancellationToken).ConfigureAwait(false);

        return customer is { IsErased: false, Contact.Email: { } email }
            ? (email, customer.Name)
            : null;
    }

    /// <summary>
    /// Hands the message over, if this deployment has anywhere to hand it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>No sender is an ordinary configuration.</strong> A deployment that has not named a
    /// mail server sends nothing, fails nothing and logs nothing per message — the host said so once
    /// at startup, which is where a fact about the whole process belongs. The port is left
    /// unregistered rather than filled with a no-op precisely so this branch is the truth rather
    /// than a silent success.
    /// </para>
    /// <para>
    /// A send that fails is logged and swallowed. What that gives up is the retry: this message is
    /// gone, and only the log line says so. The alternative — letting it throw — is a failed
    /// dispatch action and a board that stops repainting whenever a mail server is unwell, which is
    /// a far worse trade for a channel whose whole point is that a duplicate is tolerable and a miss
    /// is survivable. A durable queue of its own is what would buy the retry back; see
    /// <c>DECISIONS.local.md</c>.
    /// </para>
    /// </remarks>
    private async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (sender is null)
        {
            return;
        }

        try
        {
            await sender.SendAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            NotificationLog.Undelivered(log, failed, notification.Subject);
        }
    }
}

/// <summary>What the subscriber has to say, as source-generated log methods.</summary>
internal static partial class NotificationLog
{
    /// <remarks>
    /// Error rather than warning: a customer who was told nothing is a customer who turns up to an
    /// empty house or wonders whether their payment landed, and nothing else in the system will ever
    /// mention it. The recipient is not logged — an address is personal data, and the subject plus
    /// the correlation id is enough to find the request that meant to send it.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A customer notification ({Subject}) could not be delivered and will not be retried.")]
    internal static partial void Undelivered(ILogger logger, Exception failed, string? subject);
}
