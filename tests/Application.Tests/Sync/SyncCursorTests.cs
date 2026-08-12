using OpenDispatch.Application.Sync;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Sync;

/// <summary>
/// The cursor's trip to a device and back.
/// </summary>
/// <remarks>
/// It goes out as a string (the wire type is opaque) and comes back as whatever the device kept,
/// which may be a truncated string, an old build's format, or something a person typed. The
/// decision under test is that a cursor the server did not issue is refused rather than read as
/// "from the beginning" — because the beginning of time is a full resync of the tenant, arriving
/// over a phone connection, in answer to a typo.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SyncCursorTests
{
    [Fact]
    public void ReadsBackACursorItHandedOut()
    {
        var issued = new SyncCursor(918_273_645);

        Assert.True(SyncCursor.TryParse(issued.ToString(), out var returned));
        Assert.Equal(issued, returned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yesterday")]
    [InlineData("12.5")]
    [InlineData("-1")]
    [InlineData("1 000")]
    [InlineData("99999999999999999999")]
    public void RefusesACursorItDidNotIssue(string? text)
    {
        Assert.False(SyncCursor.TryParse(text, out var cursor));
        Assert.Equal(SyncCursor.Beginning, cursor);
    }

    /// <summary>
    /// A device that has never synced has no cursor, and the beginning is what it asks with.
    /// Nothing the server stamps can be below it, so nothing is missed on a first sync.
    /// </summary>
    [Fact]
    public void StartsBelowAnythingTheServerCanStamp()
    {
        Assert.Equal(0, SyncCursor.Beginning.Value);
    }
}
