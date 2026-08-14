using MediatR;
using Microsoft.AspNetCore.Mvc;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Attachments.GetAttachmentContent;
using OpenDispatch.Application.Attachments.ListJobAttachments;
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

        // The read half, which did not exist: until now a technician could upload a photograph that
        // nothing in this API could ever hand back — not to the office, not to the phone that took
        // it. AnyRole, because both sides of the business need to see what came off the van.
        endpoints.MapGet("/jobs/{id:guid}/attachments", ListAsync)
            .RequireAuthorization(AuthPolicies.AnyRole)
            .WithTags("Attachments")
            .WithName("ListJobAttachments")
            .Produces<IEnumerable<AttachmentResponse>>();

        endpoints.MapGet("/attachments/{id:guid}/content", ContentAsync)
            .RequireAuthorization(AuthPolicies.AnyRole)
            .WithTags("Attachments")
            .WithName("GetAttachmentContent")
            .Produces<IResult>(StatusCodes.Status200OK, "application/octet-stream");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new ListJobAttachmentsQuery(JobId.From(id)), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(captures => Results.Ok(captures.Select(ToResponse)));
    }

    /// <summary>
    /// Streams one capture's bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Results.File</c> over the store's own stream: the bytes go from the disk (or, later, the
    /// bucket) to the socket without the whole photograph being held here, and the framework
    /// disposes the stream when it has finished writing.
    /// </para>
    /// <para>
    /// <strong>Served as a download, not as a page.</strong> A content type is an instruction to a
    /// browser, and even with an allow-list of image types the safe default for bytes somebody
    /// uploaded is that they are saved rather than rendered in this API's own origin —
    /// <c>Content-Disposition: attachment</c> and <c>X-Content-Type-Options: nosniff</c> together.
    /// A client that wants to show the photograph fetches it and makes its own object URL, which is
    /// what the technician app and the board both do anyway.
    /// </para>
    /// <para>
    /// Range requests are enabled, so a phone on a bad connection resumes a large capture instead of
    /// starting again.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ContentAsync(
        Guid id,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new GetAttachmentContentQuery(AttachmentId.From(id)), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(capture =>
        {
            httpContext.Response.Headers.XContentTypeOptions = "nosniff";

            return Results.File(
                capture.Content,
                capture.ContentType,
                fileDownloadName: DownloadName(id, capture.Kind, capture.ContentType),
                enableRangeProcessing: true);
        });
    }

    /// <summary>
    /// What the file is called when it is saved: the capture's own id, so two photographs from one
    /// job never collide in a downloads folder, and an extension a person can open.
    /// </summary>
    private static string DownloadName(Guid id, Domain.Attachments.AttachmentKind kind, string contentType) =>
        $"{kind.ToString().ToLowerInvariant()}-{id}{Extension(contentType)}";

    private static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/heic" => ".heic",

        // Unreachable while the domain's allow-list is what it is. A name without an extension is
        // still a name; guessing one would be worse.
        _ => string.Empty,
    };

    private static AttachmentResponse ToResponse(AttachmentSummary capture) => new(
        capture.Id.Value,
        capture.JobId.Value,
        (Contracts.AttachmentKind)capture.Kind,
        capture.ContentType,
        capture.ByteLength,
        capture.CreatedAt,
        $"/attachments/{capture.Id.Value}/content");

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

            // What the request says it is, which the domain then refuses unless it is a kind of
            // file this system stores. It is recorded rather than sniffed because the download has
            // to answer with something, and "whatever the uploader said" bounded by an allow-list
            // is both honest and safe — see Attachment.AllowedContentTypes.
            file.ContentType,
            file.Length,
            content);

        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(uploaded => Results.Ok(new UploadAttachmentResponse(uploaded.ServerId)));
    }
}
