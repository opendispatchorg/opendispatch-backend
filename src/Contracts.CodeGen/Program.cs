using OpenDispatch.Contracts.CodeGen;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.Domain.Jobs;

// The C#-sourced half of `make gen-contracts`: everything in @opendispatch/contracts that is
// not generated from the OpenAPI document. Reads the Contracts assembly and its XML
// documentation, writes the package around them. The Makefile owns the ordering and the
// OpenAPI half; this tool owns nothing but its own output, so running it twice changes
// nothing.

const string Usage =
    "usage: dotnet run --project src/Contracts.CodeGen -- --output <directory>\n"
    + "       writes package.json, README.md and src/index.ts for " + PackageManifest.Name;

if (Argument("--output") is not { Length: > 0 } output)
{
    Console.Error.WriteLine(Usage);

    return 1;
}

try
{
    var contracts = typeof(SyncOp).Assembly;

    // Everything OpenAPI already describes is left to rest.ts (Document 3, step 52) — every
    // record and class this assembly exports whose bare name is already a components/schemas
    // entry. Enums and static-constant classes (JobStatus, BoardEvents, ...) are never
    // filtered: they are shared vocabulary a non-REST shape in this same file can still refer
    // to (JobUpdated.Status is a JobStatus; SyncJobPayload.Status too), and TypeScriptEmitter
    // only resolves a reference to another type if that type is in the set it was handed —
    // dropping JobStatus here would break the very shapes this filter exists to keep.
    var restDescribed = OpenApiSchemaNames.FromOutputDirectory(output);
    var typescriptTypes = contracts.GetExportedTypes()
        .Where(type => type.IsEnum || (type.IsAbstract && type.IsSealed) || !restDescribed.Contains(type.Name))
        .ToArray();

    // The wire shapes, then the one rule that travels with them. Both land in the same file
    // because the transition table is expressed in JobStatus and reads as a footnote to it.
    var typescript = TypeScriptEmitter.Emit(typescriptTypes, XmlDocumentation.ForAssembly(contracts))
        + JobTransitionsEmitter.Emit(Job.AllowedTransitions);

    Directory.CreateDirectory(Path.Combine(output, "src"));

    File.WriteAllText(Path.Combine(output, "src", "index.ts"), typescript);
    File.WriteAllText(Path.Combine(output, "package.json"), PackageManifest.Json);
    File.WriteAllText(Path.Combine(output, "README.md"), PackageManifest.Readme);

    Console.WriteLine($"Generated {PackageManifest.Name} into {Path.GetFullPath(output)}");

    return 0;
}
catch (Exception ex) when (ex is NotSupportedException or FileNotFoundException or IOException)
{
    // Generation refusing is a message to a person, not a stack trace: every one of these
    // names the type or the file that needs a decision.
    Console.Error.WriteLine($"Contract generation failed: {ex.Message}");

    return 1;
}

string? Argument(string name)
{
    var position = Array.IndexOf(args, name);

    return position >= 0 && position + 1 < args.Length ? args[position + 1] : null;
}
