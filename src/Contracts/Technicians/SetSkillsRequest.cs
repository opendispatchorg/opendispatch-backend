namespace OpenDispatch.Contracts.Technicians;

/// <summary>The body of <c>PUT /technicians/{id}/skills</c>.</summary>
/// <param name="Skills">The complete list they should have afterwards. Empty makes them a trainee again.</param>
public sealed record SetSkillsRequest(IReadOnlyList<string> Skills);
