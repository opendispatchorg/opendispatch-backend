namespace OpenDispatch.Contracts.Customers;

/// <summary>One customer, as the clients see them: who they are, how to reach them, and where they want work done.</summary>
/// <param name="Id">Their identity.</param>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">Their email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">Their phone number, or <see langword="null"/> if there isn't one.</param>
/// <param name="Locations">The places they want work done, in the order they were added.</param>
/// <param name="ErasedAt">
/// When they asked to be forgotten, or <see langword="null"/> if they did not. A client showing a
/// customer whose fields all read <c>[erased]</c> can say why, and an export answering a subject
/// access request carries the erasure rather than looking like data nobody filled in.
/// </param>
/// <param name="RetiredAt">
/// When they were taken off the books, or <see langword="null"/> while they are current. Retired
/// records are left out of the list, so a client that has one in hand — from a bookmark, an
/// export, or a job that predates the retirement — needs this to say why it looks inert, and to
/// know that reinstating is the way back.
/// </param>
public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    IReadOnlyList<ServiceLocationResponse> Locations,
    DateTimeOffset? ErasedAt,
    DateTimeOffset? RetiredAt);
