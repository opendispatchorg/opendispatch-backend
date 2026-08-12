namespace OpenDispatch.Contracts.Customers;

/// <summary>A customer as <c>GET /customers</c> lists them: enough to recognise and to reach, and nothing else.</summary>
/// <param name="Id">Their identity, which is what a caller picks them by.</param>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">Their email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">Their phone number, or <see langword="null"/> if there isn't one.</param>
public sealed record CustomerSummaryResponse(Guid Id, string Name, string? Email, string? Phone);
