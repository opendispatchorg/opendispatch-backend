namespace OpenDispatch.TestSupport;

/// <summary>
/// The two test categories, applied with xunit's <c>[Trait]</c>:
/// <code>[Trait(TestCategories.Name, TestCategories.Unit)]</code>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Unit"/> — pure and fast. No I/O, no container, no host. Domain, Scheduling,
/// and Application-with-fakes live here. These are the tests <c>make test-fast</c> runs,
/// and the ones worth keeping under <c>dotnet watch</c> while coding.
/// </para>
/// <para>
/// <see cref="Integration"/> — real infrastructure. A PostGIS Testcontainer, a real host
/// through <c>WebApplicationFactory</c>. Slower, and run by <c>make test</c>.
/// </para>
/// <para>
/// Constants rather than custom attributes on purpose: xunit v2 custom traits need an
/// <c>ITraitDiscoverer</c> per attribute, which is more machinery than two strings deserve.
/// </para>
/// </remarks>
public static class TestCategories
{
    /// <summary>The trait name both categories are keyed on.</summary>
    public const string Name = "Category";

    /// <summary>Pure, fast, no I/O.</summary>
    public const string Unit = "Unit";

    /// <summary>Touches real infrastructure.</summary>
    public const string Integration = "Integration";
}
