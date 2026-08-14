using FluentValidation;

namespace OpenDispatch.Application.Customers.ListCustomers;

/// <summary>
/// Shape rules for a page of customers.
/// </summary>
/// <remarks>
/// <para>
/// This request had no validator while it had no parameters. It has two now, and both are refused
/// rather than corrected: a caller asking for page zero or a page of a million has a bug, and
/// silently serving them page one — or the whole table — is how that bug survives to production
/// wearing a 200.
/// </para>
/// <para>
/// The cap is the one that matters. Without it, <c>?pageSize=1000000</c> is the unpaged query this
/// change removed, under a name that looks paged.
/// </para>
/// </remarks>
internal sealed class ListCustomersValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThan(0).WithMessage("Pages are counted from one.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, ListCustomersQuery.MaxPageSize)
            .WithMessage($"A page holds between 1 and {ListCustomersQuery.MaxPageSize} customers.");
    }
}
