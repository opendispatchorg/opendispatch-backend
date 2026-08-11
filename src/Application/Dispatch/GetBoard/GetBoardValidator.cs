using FluentValidation;

namespace OpenDispatch.Application.Dispatch.GetBoard;

/// <summary>
/// Shape rules for a board request.
/// </summary>
/// <remarks>
/// One rule, and it is here rather than left to <c>TimeWindow</c> for the reason every window in
/// this phase is: the value object refuses an inverted one by throwing, and a query built at the
/// edge would throw before any validator ran. A board asked for backwards is a caller's mistake,
/// not a 500.
/// </remarks>
internal sealed class GetBoardValidator : AbstractValidator<GetBoardQuery>
{
    public GetBoardValidator() =>
        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .WithMessage("A day cannot end before it starts.");
}
