using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Attachments.UploadAttachment;

/// <summary>
/// Shape rules for an upload.
/// </summary>
/// <remarks>
/// A size cap and a content-type sniff belong here in spirit — step 43b's own entry names them —
/// but both are properties of the request before it becomes a command (the multipart body, the
/// header a browser or client sets), not of a stream this validator is already holding. They are
/// the endpoint's business; see <c>AttachmentEndpoints</c>.
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
    }
}
