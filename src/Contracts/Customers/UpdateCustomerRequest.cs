namespace OpenDispatch.Contracts.Customers;

/// <summary>The body of <c>PUT /customers/{id}</c>.</summary>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">An email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">A phone number, or <see langword="null"/> if there isn't one.</param>
public sealed record UpdateCustomerRequest(string Name, string? Email, string? Phone);
