using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds draft <see cref="Invoice"/>s for tests. Lines are added by the test, since what
/// is on the invoice is usually the thing under test.
/// </summary>
/// <example>
/// <code>
/// var invoice = InvoiceBuilder.Any().ForJob(job.Id).Build();
/// </code>
/// </example>
public sealed record InvoiceBuilder
{
    private OrgId Org { get; init; } = OrgId.New();

    private JobId Job { get; init; } = JobId.New();

    private DateTimeOffset Issued { get; init; } = new(2026, 8, 10, 17, 0, 0, TimeSpan.Zero);

    /// <summary>An ordinary draft invoice with nothing on it yet.</summary>
    public static InvoiceBuilder Any() => new();

    /// <summary>Puts the invoice in a particular tenant.</summary>
    public InvoiceBuilder ForOrg(OrgId org) => this with { Org = org };

    /// <summary>Bills for a particular job.</summary>
    public InvoiceBuilder ForJob(JobId job) => this with { Job = job };

    /// <summary>Dates the invoice.</summary>
    public InvoiceBuilder IssuedOn(DateTimeOffset issued) => this with { Issued = issued };

    /// <summary>Creates the invoice.</summary>
    public Invoice Build() => Invoice.CreateFromJob(Org, Job, Issued);
}
