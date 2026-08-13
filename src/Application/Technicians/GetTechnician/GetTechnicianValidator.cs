using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.GetTechnician;

/// <summary>Refuses an identifier that was never minted — see <c>GetCustomerValidator</c>.</summary>
internal sealed class GetTechnicianValidator : AbstractValidator<GetTechnicianQuery>
{
    public GetTechnicianValidator() =>
        RuleFor(query => query.Id)
            .NotEqual(default(TechnicianId)).WithMessage("A technician must be named.");
}
