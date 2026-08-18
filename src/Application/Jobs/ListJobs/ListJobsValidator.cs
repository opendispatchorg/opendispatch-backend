using FluentValidation;

namespace OpenDispatch.Application.Jobs.ListJobs;

/// <summary>
/// Shape rules for a page of jobs — the same two <c>ListCustomersValidator</c> states, for the
/// same reasons.
/// </summary>
internal sealed class ListJobsValidator : AbstractValidator<ListJobsQuery>
{
    public ListJobsValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThan(0).WithMessage("Pages are counted from one.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, ListJobsQuery.MaxPageSize)
            .WithMessage($"A page holds between 1 and {ListJobsQuery.MaxPageSize} jobs.");
    }
}
