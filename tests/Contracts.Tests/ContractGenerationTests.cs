using System.Reflection;
using OpenDispatch.Contracts.CodeGen;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Contracts.Tests;

/// <summary>
/// The generator, against the wire types as they actually are. `make gen-contracts` runs the
/// whole pipeline and checks the files it produced; these are the parts of it that are ours
/// and that fail quietly — a type that never reaches the clients, an enum whose values stop
/// matching what the server sends, output that differs between two runs.
/// </summary>
/// <remarks>
/// Nothing here asserts the generated text line for line. That comparison is step 23's, it
/// belongs against the committed file rather than a copy in a test, and a test that mirrored
/// the emitter would need editing every time the emitter was right.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class ContractGenerationTests
{
    private static readonly Assembly ContractsAssembly = typeof(SyncOp).Assembly;

    private static readonly string TypeScript =
        TypeScriptEmitter.Emit(ContractsAssembly, XmlDocumentation.ForAssembly(ContractsAssembly));

    [Fact]
    public void EveryWireTypeReachesTheClients()
    {
        var missing = ContractsAssembly.GetExportedTypes()
            .Where(type => !Declares(type))
            .Select(type => type.Name)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "A type in Contracts that the generator skips is a shape the clients cannot name, "
                + $"and nothing else would say so: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void EveryEnumMemberCarriesTheNameTheServerSends()
    {
        var enums = ContractsAssembly.GetExportedTypes().Where(type => type.IsEnum).ToArray();

        Assert.NotEmpty(enums);

        // The server writes the member's name and the client compares against this literal, so
        // the two being the same string is the whole agreement.
        foreach (var name in enums.SelectMany(Enum.GetNames))
        {
            Assert.Contains($"{name}: '{name}',", TypeScript, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnOpaquePayloadArrivesAsUnknownSoAClientHasToLookBeforeUsingIt()
    {
        Assert.Contains("readonly payload: unknown;", TypeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratingTwiceProducesTheSameFile()
    {
        var again = TypeScriptEmitter.Emit(ContractsAssembly, XmlDocumentation.ForAssembly(ContractsAssembly));

        // Step 23 regenerates and diffs. Anything that varies between two runs on one machine
        // varies between two machines as well, and turns that check into noise people learn to
        // click past.
        Assert.Equal(TypeScript, again);
    }

    [Fact]
    public void RefusesATypeItCannotMapRatherThanHandingTheClientsAny()
    {
        var refusal = Assert.Throws<NotSupportedException>(
            () => TypeScriptEmitter.Emit([typeof(Unmappable)], Local));

        Assert.Contains(nameof(Uri), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnEnumThatWouldNotTravelAsItsName()
    {
        var refusal = Assert.Throws<NotSupportedException>(
            () => TypeScriptEmitter.Emit([typeof(Unconverted)], Local));

        // Without the converter the enum goes out as numbers while the generated union compares
        // against strings — a disagreement that compiles on both sides.
        Assert.Contains("JsonStringEnumConverter", refusal.Message, StringComparison.Ordinal);
    }

    private static XmlDocumentation Local =>
        XmlDocumentation.ForAssembly(typeof(ContractGenerationTests).Assembly);

    private static bool Declares(Type type) =>
        TypeScript.Contains($"export interface {type.Name} ", StringComparison.Ordinal)
        || TypeScript.Contains($"export const {type.Name} = ", StringComparison.Ordinal);

    /// <summary>A wire type nobody could serialize, to prove the generator says so.</summary>
    private sealed record Unmappable(Uri Location);

    /// <summary>An enum that would go on the wire as an integer.</summary>
    private enum Unconverted
    {
        Something = 0,
    }
}
