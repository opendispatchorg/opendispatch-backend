using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OpenDispatch.Api.OpenApi;

/// <summary>
/// The one security scheme this API has: the bearer token <c>POST /auth/login</c> issues
/// (Document 3, step 52). Every route reachable through the exported document requires it,
/// except the ones marked <c>AllowAnonymous</c> — login itself, and the liveness check.
/// </summary>
/// <remarks>
/// Two transformers rather than one. A document transformer declares the scheme once, under
/// <c>components/securitySchemes</c>; an operation transformer attaches a reference to it on
/// every operation that needs one. A single global <see cref="OpenApiDocument.Security"/>
/// requirement cannot express "except these two" — login and health are exactly the routes a
/// client has to be able to reach before it holds a token, so per-operation is not optional
/// here the way it might be on an API where everything sits behind auth.
/// </remarks>
public static class OpenApiSecurity
{
    private const string SchemeId = "Bearer";

    /// <summary>Registers the scheme and attaches it to every non-anonymous operation.</summary>
    public static OpenApiOptions AddBearerSecurityScheme(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The token POST /auth/login returns, sent as \"Authorization: Bearer <token>\".",
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            // Mirrors AuthorizationMiddleware's own rule rather than the shorter "not
            // AllowAnonymous" guess: a route needs a token only if something actually
            // required one (IAuthorizeData — RequireAuthorization's own metadata, whichever
            // policy), and AllowAnonymous overrides that regardless. GET /health has
            // neither and is reachable with no token at all today; describing it as
            // requiring one would document a rule this host does not enforce.
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var requiresAuth = metadata.OfType<IAuthorizeData>().Any()
                && !metadata.OfType<IAllowAnonymous>().Any();

            if (requiresAuth)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeId, context.Document, externalResource: null)] = [],
                });
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
