using MediatR;
using Microsoft.AspNetCore.Mvc;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Attachments.UploadAttachment;
using OpenDispatch.Contracts.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.Attachments;

/// <summary>
/// <c>POST /jobs/{id}/attachments</c> — the API half of Document 6 §9 (Document 3, step 50b): a
/// technician's phone handing over a captured photo or signature, over the storage step 43b built.
/// </summary>
/// <remarks>
/// <c>TechnicianOnly</c>, as the build text names — the second surface, beside sync (step 50), a
/// technician's own phone calls directly rather than the office's browser. Tenant-scoped the same
/// ambient way every other endpoint in this API is: the job lookup inside
/// <c>UploadAttachmentHandler</c> goes through the global query filter, so a job named from another
/// organization is not found rather than found and refused, the same answer <c>CustomerErrors
/// .NotFound</c> gives for the same reason elsewhere in this API.
/// </remarks>
public static class AttachmentEndpoints
{
    /// <summary>
    /// The largest capture this endpoint accepts.
    /// </summary>
    /// <remarks>
    /// Twenty-five megabytes: generous for a phone photograph and far below what would let one
    /// upload exhaust a host's disk before the domain ever sees it. Step 43b's own entry left "a
    /// cap and a sniff" for this step to place "at the edge with the request... before a byte
    /// reaches the store" — this is the cap. There is no content-type sniff: unlike an unbounded
    /// upload, a wrong content type costs nothing on this path (the store writes bytes under a
    /// derived key and never opens or interprets them), and Documents 6–7, which would say what the
    /// technician app actually captures, were not available this session — see
    /// <c>DECISIONS.local.md</c>.
    /// </remarks>
    internal const long MaxContentLength = 25 * 1024 * 1024;

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/jobs/{id:guid}/attachments", UploadAsync)
            .RequireAuthorization(AuthPolicies.TechnicianOnly)
            .WithTags("Attachments")
            .WithName("UploadAttachment")
            .DisableAntiforgery()
            .Produces<UploadAttachmentResponse>();

        return endpoints;
    }

    private static async Task<IResult> UploadAsync(
        Guid id,
        [FromForm] UploadAttachmentRequest request,
        IFormFile file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (request.JobId != id)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["jobId"] = ["The job in the form must match the job in the URL."],
            });
        }

        if (file.Length > MaxContentLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"An attachment cannot be larger than {MaxContentLength / (1024 * 1024)} MB."],
            });
        }

        await using var content = file.OpenReadStream();
        var command = new UploadAttachmentCommand(
            AttachmentId.From(request.AttachmentId),
            JobId.From(id),
            (Domain.Attachments.AttachmentKind)request.Kind,
            content);

        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(uploaded => Results.Ok(new UploadAttachmentResponse(uploaded.ServerId)));
    }
}
