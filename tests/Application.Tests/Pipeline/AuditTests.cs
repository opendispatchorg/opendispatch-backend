using System.Text.Json;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Customers.UpdateCustomer;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>
/// What the trail records, and what it deliberately does not.
/// </summary>
/// <remarks>
/// Through a real slice rather than a sample command, because the interesting half is the
/// reflection: an entry names the identities a command carried, and those only exist on real
/// commands. The transactional half — that an entry cannot outlive work that rolled back — is a
/// claim about a database and is proved in <c>Api.IntegrationTests</c>.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AuditTests
{
    [Fact]
    public async Task RecordsWhoDidWhatToWhich()
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));
        await slice.Send(new UpdateCustomerCommand(created.Value, "Vance Refrigeration Ltd", null, null));

        Assert.Equal(["CreateCustomer", "UpdateCustomer"], slice.Audit.Entries.Select(entry => entry.Action));

        var update = slice.Audit.Entries[^1];

        Assert.Equal(slice.Caller, update.UserId);
        Assert.Equal(SliceHost.CallerName, update.Username);
        Assert.Equal(slice.Tenant, update.OrgId);
        Assert.Equal(slice.Clock.UtcNow, update.At);

        // The identity the command named, so "who changed customer X" is answerable.
        var targets = JsonSerializer.Deserialize<Dictionary<string, Guid>>(update.Targets)!;
        Assert.Equal(created.Value.Value, Assert.Contains(nameof(UpdateCustomerCommand.Id), targets));
    }

    /// <summary>
    /// The limit that makes the trail and erasure compatible: it records the act, never the values.
    /// </summary>
    /// <remarks>
    /// A trail holding a customer's name and phone number would be a second, append-only copy of the
    /// personal data an erasure request has to remove — and erasing somebody would mean rewriting
    /// history to keep a promise.
    /// </remarks>
    [Fact]
    public async Task RecordsNothingAnybodyTyped()
    {
        await using var slice = SliceHost.Customers();

        // Deliberately nothing in common with the caller's own username, which the entry does
        // carry: what must not appear is what the customer typed about themselves.
        await slice.Send(new CreateCustomerCommand("Whitlock Heating", "office@whitlock.example", "+44 20 7946 0000"));

        var entry = Assert.Single(slice.Audit.Entries);
        var everything = entry.Action + entry.Targets + entry.Username;

        Assert.DoesNotContain("Whitlock", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("office@", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7946", everything, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused command changed nothing, so there is nothing to attribute. The trail is a record of
    /// acts, not of attempts — and one that logged every validation failure would bury the acts.
    /// </summary>
    [Fact]
    public async Task RecordsNothingForACommandThatWasRefused()
    {
        await using var slice = SliceHost.Customers();

        var refused = await slice.Send(new CreateCustomerCommand("", null, null));
        Assert.True(refused.IsFailure);

        var missing = await slice.Send(new UpdateCustomerCommand(CustomerId.New(), "Nobody", null, null));
        Assert.True(missing.IsFailure);

        Assert.Empty(slice.Audit.Entries);
    }

    /// <summary>A query is not an act: reading a customer records nothing.</summary>
    [Fact]
    public async Task RecordsNothingForAQuery()
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));
        await slice.Send(new GetCustomerQuery(created.Value));

        Assert.Single(slice.Audit.Entries);
    }
}
