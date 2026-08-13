using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Auth;

namespace OpenDispatch.Api.Board;

/// <summary>
/// The real-time dispatch board (Document 2 §9, step 51): one connection per browser tab, added to
/// its organization's group on connect and pushed <c>job.updated</c>/<c>assignment.updated</c>
/// events for the life of the connection.
/// </summary>
/// <remarks>
/// <para>
/// <c>AdminOrDispatcher</c>, matching <c>GET /dispatch/board</c> beside it (step 48) — the office
/// roles that watch the board live, not the field's. Document 2 §9 says "dispatchers join their
/// org's group," read the same way step 47's own genuinely-ambiguous role calls were: the two
/// roles that operate the board day to day, not the persona split's third member.
/// </para>
/// <para>
/// No client-invokable method exists on this hub, on purpose. Document 2 §9 describes push only —
/// a client applies what it is sent to the board it already fetched over REST — and a hub method
/// nothing calls would be surface invented ahead of a caller, the same restraint this phase has
/// applied everywhere else.
/// </para>
/// </remarks>
[Authorize(Policy = AuthPolicies.AdminOrDispatcher)]
public sealed class DispatchHub : Hub
{
    /// <summary>
    /// Joins the connection to its tenant's group.
    /// </summary>
    /// <remarks>
    /// Reads the org claim directly off <see cref="HubCallerContext.User"/> rather than through
    /// <c>ITenantContext</c>: a hub invocation does not share the HTTP request's scope that
    /// populated it (SignalR resolves a fresh scope per invocation), so the ambient tenant
    /// middleware step 45 built for ordinary endpoints has nothing to hand this one. Every token
    /// this system issues carries the claim (the same "in ordinary operation this always
    /// succeeds" reasoning steps 45/50 already rely on for the same claim), so a connection
    /// missing or failing to parse it is a token this system did not issue in the usual way, and
    /// is refused rather than silently joining no group.
    /// </remarks>
    public override async Task OnConnectedAsync()
    {
        if (!TryGetOrgId(Context.User, out var org))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroups.NameFor(org)).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    private static bool TryGetOrgId(ClaimsPrincipal? user, out OrgId org)
    {
        org = default;
        var claim = user?.FindFirst(AuthClaimTypes.Org)?.Value;

        if (!Guid.TryParse(claim, out var value))
        {
            return false;
        }

        org = OrgId.From(value);

        return true;
    }
}
