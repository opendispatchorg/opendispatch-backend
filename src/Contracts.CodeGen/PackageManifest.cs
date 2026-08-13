namespace OpenDispatch.Contracts.CodeGen;

/// <summary>
/// The npm packaging around the generated TypeScript: what the package is called, where its
/// two entry points are, and a note to whoever opens the directory expecting to edit it.
/// </summary>
/// <remarks>
/// <para>
/// Generated rather than committed by hand so that everything under <c>contracts/</c> has one
/// origin. A directory where some files are generated and others are not is a directory nobody
/// can safely delete and rebuild, and the drift check in step 23 wants to diff the whole thing.
/// </para>
/// <para>
/// Two entry points rather than one barrel, because the two halves come from different places
/// and, before step 52, named the same things far more than either needed to: any REST DTO
/// declared in C# was swept into the root entry point too, even though the OpenAPI half already
/// described it. The root now holds only what OpenAPI cannot — SignalR events, the opaque sync
/// payloads' typed shapes, and the enums both halves still legitimately share (a REST DTO
/// carrying a <c>JobStatus</c> makes OpenAPI emit its own copy of that one small type, which a
/// single flat re-export would still collide on — the reason for two entry points at all,
/// narrower now than it used to be). <c>/rest</c> is whatever the OpenAPI document currently
/// describes; see <see cref="OpenApiSchemaNames"/> for how the root avoids repeating it.
/// </para>
/// </remarks>
public static class PackageManifest
{
    /// <summary>The package name both client repositories depend on.</summary>
    public const string Name = "@opendispatch/contracts";

    /// <summary>The version every published release starts numbering from.</summary>
    /// <remarks>
    /// Bumped by hand, in this one place, when a release is actually cut — <c>make
    /// gen-contracts</c> has no way to know whether a change is a breaking one, an addition, or
    /// a fix, so it does not guess at semver on a caller's behalf. <c>0.x</c> until the contract
    /// itself has shipped a stable major version: every consumer today is still inside this same
    /// monorepo's own client repositories, not an external integrator who needs the stability
    /// promise <c>1.0.0</c> makes.
    /// </remarks>
    public const string Version = "0.1.0";

    /// <summary><c>package.json</c>, as JSON text.</summary>
    /// <remarks>
    /// Not <c>private</c>, and a real version (Document 3, step 52) — the TEMPORARY path/git
    /// reference step 23 documented is exactly what a real, installable release makes
    /// unnecessary. <c>publishConfig.access</c> is <c>public</c> because a scoped package name
    /// (<c>@opendispatch/...</c>) defaults to a paid private publish otherwise; nothing about
    /// this package is meant to be paid for or hidden.
    /// </remarks>
    public static string Json =>
        $$"""
        {
          "name": "@opendispatch/contracts",
          "version": "{{Version}}",
          "description": "Shared OpenDispatch wire types: SignalR board events, offline-sync payloads, and the generated REST surface.",
          "license": "Apache-2.0",
          "type": "module",
          "types": "./src/index.ts",
          "exports": {
            ".": "./src/index.ts",
            "./rest": "./src/rest.ts"
          },
          "files": [
            "src",
            "openapi.json"
          ],
          "sideEffects": false,
          "publishConfig": {
            "access": "public"
          }
        }

        """.ReplaceLineEndings("\n");

    /// <summary><c>README.md</c>, as Markdown text.</summary>
    public static string Readme =>
        $$"""
        # @opendispatch/contracts

        The API contract the OpenDispatch backend owns and the web and mobile clients consume.

        **Generated. Do not edit.** Everything in this directory is produced by
        `make gen-contracts` in the backend repository. Change the C# or an endpoint, regenerate,
        and commit — that is what turns a backend change into a client compile error rather than a
        runtime surprise. The backend's CI regenerates and diffs on every push, so a stale copy
        fails a build there before it reaches anyone here.

        | Import | Generated from | What it holds |
        | --- | --- | --- |
        | `@opendispatch/contracts` | `src/Contracts` (C#) | SignalR board events, offline-sync payloads, shared enums |
        | `@opendispatch/contracts/rest` | `openapi.json` | Request and response types for the REST endpoints |

        The split is not cosmetic. OpenAPI cannot describe a SignalR message or a sync batch, so
        those shapes are written once in C# and emitted here; the REST half is generated from the
        OpenAPI document the API exports. `openapi.json` ships alongside for anything that would
        rather generate its own client.

        ```ts
        import { BoardEvents, canTransition, type JobUpdated } from '@opendispatch/contracts';

        connection.on(BoardEvents.JobUpdated, (event: JobUpdated) => board.apply(event));

        // The job state machine, exported from the same table the server enforces — so an
        // offline device offers the actions the server will actually accept.
        canTransition(job.status, 'InProgress');
        ```

        ## Depending on it

        Published under this name — no path or git reference needed:

        ```json
        {
          "dependencies": {
            "@opendispatch/contracts": "^{{Version}}"
          }
        }
        ```

        The package ships TypeScript source rather than compiled output, which most of it being
        types makes reasonable — but `BoardEvents`, `jobTransitions` and `canTransition` are real
        values, so a bundler that does not transform linked dependencies needs telling
        (`transpilePackages` in Next, `optimizeDeps` in Vite).

        """.ReplaceLineEndings("\n");
}
