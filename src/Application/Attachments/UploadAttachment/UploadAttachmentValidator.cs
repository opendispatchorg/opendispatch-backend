using FluentValidation;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Attachments.UploadAttachment;

/// <summary>
/// Shape rules for an upload.
/// </summary>
/// <remarks>
/// The size <em>cap</em> is still the endpoint's — it is a property of the multipart body, and
/// refusing it there is what stops the bytes being read at all. What the command can be asked is
/// whether the caller declared a kind of file this system stores and a length above nothing, and
/// both are asked here so the answer is a 400 that names the field rather than a domain exception
/// on the way to a 500.
/// </remarks>
internal sealed class UploadAttachmentValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentValidator()
    {
        RuleFor(command => command.AttachmentId)
            .NotEqual(default(AttachmentId)).WithMessage("An attachment must carry the id its device gave it.");

        RuleFor(command => command.JobId)
            .NotEqual(default(JobId)).WithMessage("An attachment must belong to a job.");

        RuleFor(command => command.Kind)
            .IsInEnum().WithMessage("That is not a kind of attachment.");

        RuleFor(command => command.Content)
            .NotNull().WithMessage("An upload must carry content.");

        // The domain refuses these too, and refusing them here as well is what turns "this system
        // does not store that" from a 500 into a 400 that names the field. The list itself is the
        // domain's — a second copy here would be the thing that drifts.
        RuleFor(command => command.ContentType)
            .Must(type => type is not null && Attachment.AllowedContentTypes.Contains(type.Trim()))
            .WithMessage(
                "An attachment must be a photograph or a signature: "
                    + string.Join(", ", Attachment.AllowedContentTypes.Order(StringComparer.Ordinal)) + ".");

        RuleFor(command => command.ByteLength)
            .GreaterThan(0).WithMessage("An upload must carry some content.");
    }
}
