using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Customers;

/// <summary>
/// A customer and the places they want work done.
/// </summary>
/// <remarks>
/// The locations are owned, not referenced: they are created, read and removed only through
/// the customer, and there is no way to hold one without holding the customer it belongs to.
/// A job crossing that boundary takes the location's id, not the location.
/// </remarks>
public sealed class Customer : AggregateRoot
{
    private readonly List<ServiceLocation> _locations = [];

    // Materialisation constructor — see the note on Job.
    private Customer() => Name = string.Empty;

    private Customer(CustomerId id, OrgId orgId, string name, ContactInfo contact)
    {
        Id = id;
        OrgId = orgId;
        Name = name;
        Contact = contact;
    }

    /// <summary>This customer's identity.</summary>
    public CustomerId Id { get; private set; }

    /// <summary>The tenant whose books they are on.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>Their name, personal or trading.</summary>
    public string Name { get; private set; }

    /// <summary>How to reach them.</summary>
    public ContactInfo Contact { get; private set; }

    /// <summary>The places they want work done, in the order they were added.</summary>
    public IReadOnlyList<ServiceLocation> Locations => _locations.AsReadOnly();

    /// <summary>
    /// When they were erased at their own request, or <see langword="null"/> if they were not.
    /// </summary>
    public DateTimeOffset? ErasedAt { get; private set; }

    /// <summary>Whether their personal data has been erased.</summary>
    public bool IsErased => ErasedAt is not null;

    /// <summary>Takes on a new customer, with no locations yet.</summary>
    /// <exception cref="DomainException">The customer has no name.</exception>
    public static Customer Create(OrgId orgId, string name, ContactInfo contact)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A customer must have a name.");
        }

        return new Customer(CustomerId.New(), orgId, name.Trim(), contact);
    }

    /// <summary>
    /// Adds a place they want work done, and hands back its identity — which is what a job
    /// needs in order to point at it.
    /// </summary>
    /// <exception cref="DomainException">The location could not be found or driven to.</exception>
    public ServiceLocationId AddLocation(string label, string address, GeoPoint point)
    {
        RefuseIfErased();

        var location = ServiceLocation.Create(label, address, point);
        _locations.Add(location);

        return location.Id;
    }

    /// <summary>
    /// Takes a location off their record.
    /// </summary>
    /// <exception cref="DomainException">
    /// They have no such location. Unlike a technician's skills, this refuses rather than
    /// shrugging: removing a location the caller believes exists means the caller is working
    /// from a different picture of the world, and silently succeeding would hide that.
    /// </exception>
    public void RemoveLocation(ServiceLocationId locationId)
    {
        RefuseIfErased();

        if (_locations.RemoveAll(location => location.Id == locationId) == 0)
        {
            throw new DomainException($"This customer has no service location {locationId.Value}.");
        }
    }

    /// <summary>Corrects a location already on their books.</summary>
    /// <exception cref="DomainException">
    /// They have no such location, or the correction leaves it with no label or no address.
    /// </exception>
    public void UpdateLocation(ServiceLocationId locationId, string label, string address, GeoPoint point)
    {
        RefuseIfErased();

        var location = _locations.Find(candidate => candidate.Id == locationId)
            ?? throw new DomainException($"This customer has no service location {locationId.Value}.");

        location.Update(label, address, point);
    }

    /// <summary>Corrects their name.</summary>
    /// <exception cref="DomainException">The customer would have no name.</exception>
    public void Rename(string name)
    {
        RefuseIfErased();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A customer must have a name.");
        }

        Name = name.Trim();
    }

    /// <summary>Replaces how to reach them.</summary>
    public void SetContact(ContactInfo contact)
    {
        RefuseIfErased();

        Contact = contact;
    }

    /// <summary>
    /// Erases everything on this record that says who they are: their name, their contact details,
    /// and the label, address and coordinates of every place they wanted work done.
    /// </summary>
    /// <param name="at">When the erasure was asked for.</param>
    /// <remarks>
    /// <para>
    /// <strong>Overwriting, not deleting, and the difference is the point.</strong> A shop is
    /// required to erase personal data on request and required to keep its financial records; a hard
    /// delete would take the jobs and the invoices with it and satisfy neither. So the row survives
    /// with nothing personal in it, and every job, stop and invoice still references it, still dated
    /// and still totalling what it totalled.
    /// </para>
    /// <para>
    /// <strong>The jobs are not this aggregate's to erase.</strong> A job carries its own copy of
    /// the location's coordinates and whatever the technician wrote about the visit — both personal
    /// — and it is a separate aggregate, so <see cref="Jobs.Job.Erase"/> is what reaches them. What
    /// makes the two one act is that they are one command and one transaction.
    /// </para>
    /// <para>
    /// <strong>Idempotent.</strong> Erasing twice does nothing the second time rather than failing:
    /// this is reached by an operator answering a legal request, possibly through a retry, and
    /// "already erased" is the outcome they wanted.
    /// </para>
    /// </remarks>
    public void Erase(DateTimeOffset at)
    {
        if (IsErased)
        {
            return;
        }

        Name = Tombstone.Text;
        Contact = ContactInfo.None;

        foreach (var location in _locations)
        {
            location.Erase();
        }

        ErasedAt = at;
    }


    /// <summary>
    /// When they were retired, or <see langword="null"/> while they are still current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Retiring is not deleting, and this system has no delete.</strong> A customer's name is on their jobs and their invoices, and a technician's is on every stop they ever drove. A hard
    /// delete would orphan those references and there is nothing built to handle that, so what a
    /// shop actually needs — "stop offering them, keep the history" — is a flag and a filtered read.
    /// </para>
    /// <para>
    /// It is not erasure either. An erasure destroys what says who somebody is and is irreversible;
    /// this hides a record from the lists people pick from and can be undone tomorrow.
    /// </para>
    /// </remarks>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Whether they are still on the books.</summary>
    public bool IsActive => RetiredAt is null;

    /// <summary>Retires them: still in the history, no longer offered.</summary>
    /// <param name="at">When.</param>
    /// <remarks>Retiring somebody already retired does nothing, so a repeated click is harmless.</remarks>
    public void Retire(DateTimeOffset at)
    {
        RefuseIfErased();

        RetiredAt ??= at;
    }

    /// <summary>Puts them back on the books.</summary>
    public void Reinstate()
    {
        RefuseIfErased();

        RetiredAt = null;
    }

    /// <exception cref="DomainException">
    /// The customer has been erased. Editing them back into existence — a name typed in again, a
    /// new address added to the record — would undo an erasure the shop has promised, so the
    /// aggregate refuses rather than leaving it to whoever calls.
    /// </exception>
    private void RefuseIfErased()
    {
        if (IsErased)
        {
            throw new DomainException("An erased customer cannot be changed.");
        }
    }
}
