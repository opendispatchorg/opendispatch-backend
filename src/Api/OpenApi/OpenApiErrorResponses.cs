using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OpenDispatch.Api.OpenApi;

/// <summary>
/// One <c>default</c> response, added to every operation, describing the shape every failure
/// this API produces actually has (Document 3, step 52).
/// </summary>
/// <remarks>
/// <para>
/// Every non-2xx this host writes goes through exactly one of two places —
/// <see cref="OpenDispatch.Api.ErrorHandling.ResultHttpMapping.ToProblem"/> or
/// <see cref="OpenDispatch.Api.ErrorHandling.UnhandledExceptionHandler"/> — and both write a
/// <see cref="ProblemDetails"/> body (RFC 7807). Which status code a given route can actually
/// answer with depends on which <c>ErrorCategory</c> its own handler can produce, a fact that
/// lives in the handler, not in routing metadata an operation transformer can see — so rather
/// than guess a per-route list of status codes that would drift the day a handler's own
/// business rules change, this documents the one fact that is true everywhere: whatever the
/// status code, the body is a <see cref="ProblemDetails"/>. OpenAPI's own <c>default</c> response
/// key means exactly that — "the shape of anything not otherwise listed."
/// </para>
/// <para>
/// A document transformer, not an operation transformer: it needs
/// <see cref="OpenApiDocumentTransformerContext.GetOrCreateSchemaAsync"/> to register
/// <see cref="ProblemDetails"/> once under <c>components/schemas</c> rather than inlining a copy
/// into every operation, and it runs once over every path rather than being wired into each of
/// the twelve endpoint files individually — the same "mechanical, not type by type" register
/// <c>TypeScriptEmitter</c> already applies to the other half of this contract.
/// </para>
/// </remarks>
public static class OpenApiErrorResponses
{
    private const string ContentType = "application/problem+json";

    /// <summary>Adds the shared <c>default</c> response to every operation in the document.</summary>
    public static OpenApiOptions AddDefaultErrorResponse(this OpenApiOptions options)
    {
        options.AddDocumentTransformer(async (document, context, cancellationToken) =>
        {
            // Registered once under components/schemas and referenced everywhere else, rather
            // than the schema GetOrCreateSchemaAsync hands back being inlined into every one of
            // this API's operations — twenty-eight identical copies of the same object would
            // otherwise bloat the document (and the TypeScript openapi-typescript generates
            // from it) for no reason a reader benefits from.
            var schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), null, cancellationToken)
                .ConfigureAwait(false);

            document.Components ??= new OpenApiComponents();
            document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
            document.Components.Schemas["ProblemDetails"] = schema;

            foreach (var pathItem in document.Paths.Values)
            {
                if (pathItem.Operations is not { } operations)
                {
                    continue;
                }

                foreach (var operation in operations.Values)
                {
                    operation.Responses ??= new OpenApiResponses();
                    operation.Responses["default"] = new OpenApiResponse
                    {
                        Description = "An error. Every failure this API returns — validation, "
                            + "not found, conflict, unauthorized, or unhandled — is a ProblemDetails body.",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            [ContentType] = new()
                            {
                                Schema = new OpenApiSchemaReference("ProblemDetails", document, externalResource: null),
                            },
                        },
                    };
                }
            }
        });

        return options;
    }
}
