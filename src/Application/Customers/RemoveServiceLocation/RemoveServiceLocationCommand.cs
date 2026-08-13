using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.RemoveServiceLocation;

/// <summary>Takes a place off a customer's record.</summary>
/// <param name="CustomerId">Whose record it is on.</param>
/// <param name="LocationId">Which location.</param>
/// <remarks>
/// Nothing checks whether a job still points at this location — <c>Job.LocationId</c> has no
/// foreign key to it (step 26 could not express one across the aggregate boundary), so a job
/// created against a site that is later removed keeps the id and the coordinates it was booked
/// with. That gap has carried since step 10; this step is only what makes it reachable, not what
/// closes it.
/// </remarks>
public sealed record RemoveServiceLocationCommand(CustomerId CustomerId, ServiceLocationId LocationId) : ICommand;
