using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenDispatch.Api.Configuration;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Attachments;

/// <summary>
/// Which store a deployment gets, and what it is refused for leaving half-configured.
/// </summary>
/// <remarks>
/// Worth testing for the same reason the startup guards beside it are: the failure is invisible
/// while it works. A host that started with neither a directory nor a bucket would serve every
/// request perfectly until the first technician uploaded a photograph, and a host that refused to
/// start because it named a bucket and no longer needed a directory would be an outage on the
/// deploy that moved storage — the one deploy nobody wants a surprise on.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AttachmentOptionsTests
{
    private const string Endpoint = "https://accountid.r2.cloudflarestorage.com";

    [Fact]
    public void AHostWithNeitherADirectoryNorABucketDoesNotStart()
    {
        var refused = Assert.Throws<OptionsValidationException>(() => Resolve([]));

        Assert.Contains("Attachments:Root", string.Join(" ", refused.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// The directory stops being required the moment a bucket is named — otherwise every
    /// deployment that moved its photographs into an object store would still have to carry a path
    /// nothing writes to, which is exactly the sort of leftover that later gets "cleaned up" and
    /// takes the host down.
    /// </summary>
    [Fact]
    public void ABucketMakesTheDirectoryUnnecessary()
    {
        var attachments = Resolve(new Dictionary<string, string?>
        {
            ["Attachments:Bucket"] = "opendispatch-attachments",
            ["Attachments:ServiceUrl"] = Endpoint,
            ["Attachments:Region"] = "auto",
        });

        Assert.True(attachments.UsesObjectStore);
        Assert.Equal(string.Empty, attachments.Root);
    }

    /// <summary>
    /// A bucket with no way to reach it: neither an endpoint for an S3-compatible store nor a
    /// region for Amazon's own. Caught at startup rather than at the first upload, where the SDK's
    /// own failure names neither the setting nor this application.
    /// </summary>
    [Fact]
    public void ABucketWithNoAddressDoesNotStart()
    {
        var refused = Assert.Throws<OptionsValidationException>(() => Resolve(new Dictionary<string, string?>
        {
            ["Attachments:Bucket"] = "opendispatch-attachments",
        }));

        Assert.Contains("Attachments:ServiceUrl", string.Join(" ", refused.Failures), StringComparison.Ordinal);
    }

    /// <summary>Amazon S3 proper, where the region is the address.</summary>
    [Fact]
    public void ARegionAloneIsEnoughForAmazon()
    {
        var attachments = Resolve(new Dictionary<string, string?>
        {
            ["Attachments:Bucket"] = "opendispatch-attachments",
            ["Attachments:Region"] = "eu-west-2",
        });

        Assert.True(attachments.UsesObjectStore);
        Assert.Equal("opendispatch-attachments", attachments.ToStorageSettings().Bucket);
    }

    private static AttachmentOptions Resolve(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddAttachmentOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<AttachmentOptions>>().Value;
    }
}
