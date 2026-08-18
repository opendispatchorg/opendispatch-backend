using System.Collections.Concurrent;
using System.Text.Json;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Turns a domain event into a row and back again.
/// </summary>
/// <remarks>
/// <para>
/// Persistence, not messaging: an event crossing process boundaries is this layer's problem, the
/// same way the shape of a <c>Job</c>'s row is. Nothing above here knows the outbox stores JSON.
/// </para>
/// <para>
/// The type is carried as a full name and resolved inside the Domain assembly — where every event
/// lives and is required to keep living. A name that no longer resolves is a message that cannot be
/// delivered, which is loud (the dispatcher records the failure on the row) rather than silent, and
/// is the reason renaming an event means draining the outbox first.
/// </para>
/// <para>
/// Default serializer options on purpose: these rows are read by this application and by a human
/// with psql, never by a client, so there is no contract to keep and no naming policy to agree on.
/// </para>
/// </remarks>
internal static class DomainEventSerializer
{
    private static readonly ConcurrentDictionary<string, Type> Types = new();

    /// <summary>The event as it is stored.</summary>
    public static string Serialize(IDomainEvent domainEvent) =>
        JsonSerializer.Serialize(domainEvent, domainEvent?.GetType() ?? typeof(IDomainEvent));

    /// <summary>
    /// The event a row describes.
    /// </summary>
    /// <param name="type">The stored type name.</param>
    /// <param name="payload">The stored JSON.</param>
    /// <exception cref="InvalidOperationException">
    /// The name does not resolve to a domain event this build knows — a message written by a
    /// version that has since renamed or removed the type.
    /// </exception>
    public static IDomainEvent Deserialize(string type, string payload)
    {
        var resolved = Types.GetOrAdd(type, static name => typeof(IDomainEvent).Assembly.GetType(name)
            ?? throw new InvalidOperationException(
                $"'{name}' is not a domain event this build knows. An outbox message written by an "
                    + "earlier version cannot be delivered by one that has renamed or removed its type; "
                    + "drain the outbox before such a rename."));

        return (IDomainEvent)JsonSerializer.Deserialize(payload, resolved)!;
    }
}
