using System.Text.Json;

namespace OpenDispatch.Contracts.CodeGen;

/// <summary>
/// The names OpenAPI already describes — <c>components/schemas</c> in the exported document —
/// so the C#-sourced half of the package (Document 3, step 52) can leave them to it rather than
/// emit a second, competing copy.
/// </summary>
/// <remarks>
/// Reads <c>openapi.json</c> from the same output directory <c>Contracts.CodeGen</c> writes
/// into, rather than taking a second command-line argument for it: the Makefile already copies
/// the export there before running this tool
/// (<c>cp $(OPENAPI_EXPORT) $(CONTRACTS)/openapi.json</c>, ahead of
/// <c>dotnet run --project src/Contracts.CodeGen ... -- --output $(CONTRACTS)</c>), so the file
/// this tool needs is already sitting exactly where <c>--output</c> already points. One fewer
/// argument to keep in step with the Makefile is one fewer way for the two to drift.
/// </remarks>
public static class OpenApiSchemaNames
{
    /// <summary>
    /// The bare type names <c>openapi.json</c>'s <c>components/schemas</c> already covers.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// <paramref name="outputDirectory"/> has no <c>openapi.json</c> — this tool was run out of
    /// the order <c>make gen-contracts</c> itself keeps, ahead of the OpenAPI export it depends
    /// on to know what not to duplicate.
    /// </exception>
    public static IReadOnlySet<string> FromOutputDirectory(string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, "openapi.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{path} does not exist. This tool reads the OpenAPI export to know which "
                    + "Contracts types the REST half of the package already describes — run it "
                    + "through `make gen-contracts`, which stages openapi.json here first, "
                    + "rather than directly.",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("components", out var components)
            || !components.TryGetProperty("schemas", out var schemas))
        {
            return new HashSet<string>();
        }

        return schemas.EnumerateObject().Select(schema => schema.Name).ToHashSet(StringComparer.Ordinal);
    }
}
