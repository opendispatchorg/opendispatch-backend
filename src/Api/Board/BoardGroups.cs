using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.Board;

/// <summary>
/// The one naming rule for a tenant's SignalR group — shared so <see cref="DispatchHub"/> joining
/// a connection to it and <see cref="SignalRBoardNotifier"/> publishing to it cannot quietly drift
/// into two different strings for the same organization.
/// </summary>
internal static class BoardGroups
{
    /// <summary>The group a tenant's dispatch board subscribers sit in.</summary>
    internal static string NameFor(OrgId org) => org.Value.ToString();
}
