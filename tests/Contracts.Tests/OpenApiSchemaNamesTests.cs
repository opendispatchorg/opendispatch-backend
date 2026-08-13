using OpenDispatch.Contracts.CodeGen;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Contracts.Tests;

/// <summary>
/// What tells the C#-sourced half of the package (Document 3, step 52) not to repeat a shape
/// OpenAPI already describes — read directly against real JSON rather than assumed correct.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class OpenApiSchemaNamesTests
{
    [Fact]
    public void ReadsEveryComponentSchemaName()
    {
        using var directory = AWrittenOpenApiJson(
            """
            {
              "components": {
                "schemas": {
                  "CreateJobRequest": {},
                  "JobResponse": {}
                }
              }
            }
            """);

        var names = OpenApiSchemaNames.FromOutputDirectory(directory.Path);

        Assert.Equal(new HashSet<string> { "CreateJobRequest", "JobResponse" }, names);
    }

    [Fact]
    public void AnEmptySchemasObjectIsNoNamesRatherThanAnError()
    {
        using var directory = AWrittenOpenApiJson("""{ "components": { "schemas": {} } }""");

        Assert.Empty(OpenApiSchemaNames.FromOutputDirectory(directory.Path));
    }

    [Fact]
    public void ADocumentWithNoComponentsAtAllIsNoNamesRatherThanAnError()
    {
        using var directory = AWrittenOpenApiJson("""{ "openapi": "3.1.0" }""");

        Assert.Empty(OpenApiSchemaNames.FromOutputDirectory(directory.Path));
    }

    [Fact]
    public void RefusesWithTheMissingPathWhenNothingWasStagedThereYet()
    {
        using var directory = ATemporaryDirectory();

        var refusal = Assert.Throws<FileNotFoundException>(
            () => OpenApiSchemaNames.FromOutputDirectory(directory.Path));

        Assert.Contains("openapi.json", refusal.Message, StringComparison.Ordinal);
    }

    private static TemporaryDirectory AWrittenOpenApiJson(string json)
    {
        var directory = ATemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "openapi.json"), json);

        return directory;
    }

    private static TemporaryDirectory ATemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opendispatch-openapi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return new TemporaryDirectory(path);
    }

    /// <summary>A directory this test made and this test removes, win or lose.</summary>
    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
