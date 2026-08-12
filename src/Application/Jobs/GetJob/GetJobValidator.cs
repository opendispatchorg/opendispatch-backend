using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Jobs.GetJob;

/// <summary>Refuses an identifier that was never minted — see <c>GetCustomerValidator</c>.</summary>
internal sealed class GetJobValidator : AbstractValidator<GetJobQuery>
{
    public GetJobValidator() =>
        RuleFor(query => query.Id)
            .NotEqual(default(JobId)).WithMessage("A job must be named.");
}
