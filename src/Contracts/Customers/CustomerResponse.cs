namespace OpenDispatch.Contracts.Customers;

/// <summary>One customer, as the clients see them: who they are, how to reach them, and where they want work done.</summary>
/// <param name="Id">Their identity.</param>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">Their email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">Their phone number, or <see langword="null"/> if there isn't one.</param>
/// <param name="Locations">The places they want work done, in the order they were added.</param>
public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    IReadOnlyList<ServiceLocationResponse> Locations);
