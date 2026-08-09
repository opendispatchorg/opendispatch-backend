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
/// and will eventually name the same things: a REST DTO carrying a <c>JobStatus</c> makes
/// OpenAPI emit its own copy, and a single flat re-export would then have two. The root is the
/// C#-sourced surface; <c>/rest</c> is whatever the OpenAPI document currently describes.
/// </para>
/// </remarks>
public static class PackageManifest
{
    /// <summary>The package name both client repositories depend on.</summary>
    public const string Name = "@opendispatch/contracts";

    /// <summary><c>package.json</c>, as JSON text.</summary>
    /// <remarks>
    /// <c>private</c> until step 52 configures publishing; until then the clients consume this
    /// by path or git reference, which needs no version and must not be published by accident.
    /// </remarks>
    public static string Json =>
        """
        {
          "name": "@opendispatch/contracts",
          "version": "0.0.0",
          "private": true,
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
          "sideEffects": false
        }

        """.ReplaceLineEndings("\n");

    /// <summary><c>README.md</c>, as Markdown text.</summary>
    public static string Readme =>
        """
        # @opendispatch/contracts

        The API contract the OpenDispatch backend owns and the web and mobile clients consume.

        **Generated. Do not edit.** Everything in this directory is produced by
        `make gen-contracts` in the backend repository. Change the C# or an endpoint, regenerate,
        and commit — that is what turns a backend change into a client compile error rather than a
        runtime surprise.

        | Import | Generated from | What it holds |
        | --- | --- | --- |
        | `@opendispatch/contracts` | `src/Contracts` (C#) | SignalR board events, offline-sync payloads, shared enums |
        | `@opendispatch/contracts/rest` | `openapi.json` | Request and response types for the REST endpoints |

        The split is not cosmetic. OpenAPI cannot describe a SignalR message or a sync batch, so
        those shapes are written once in C# and emitted here; the REST half is generated from the
        OpenAPI document the API exports. `openapi.json` ships alongside for anything that would
        rather generate its own client.

        ```ts
        import { BoardEvents, type JobUpdated } from '@opendispatch/contracts';

        connection.on(BoardEvents.JobUpdated, (event: JobUpdated) => board.apply(event));
        ```

        """.ReplaceLineEndings("\n");
}
