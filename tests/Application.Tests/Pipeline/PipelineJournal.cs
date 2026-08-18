namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>
/// What the pipeline did, in the order it did it.
/// </summary>
/// <remarks>
/// <para>
/// The handler and the unit of work both write here, so one assertion says the whole story —
/// that a transaction was opened, that the handler ran inside it, and that the work was kept or
/// taken back. Separate spies would each prove their own half and neither would prove the
/// ordering, which is the thing this step actually decides.
/// </para>
/// <para>
/// The entries are what a database would notice, not what the code did to bring it about. A
/// transaction abandoned by disposal and one asked to roll back are the same entry, so
/// rearranging <em>how</em> the behavior rolls back does not break a test that is about
/// <em>whether</em> it does.
/// </para>
/// </remarks>
internal sealed class PipelineJournal
{
    /// <summary>A transaction was opened.</summary>
    public const string Begun = "transaction begun";

    /// <summary>The handler ran.</summary>
    public const string Handled = "handler ran";

    /// <summary>Staged changes were written.</summary>
    public const string Saved = "changes saved";

    /// <summary>The transaction was committed.</summary>
    public const string Committed = "committed";

    /// <summary>
    /// The transaction ended without being committed — asked to roll back, or disposed while
    /// still open, which is the same thing to a database.
    /// </summary>
    public const string RolledBack = "rolled back";

    /// <summary>
    /// An attempt ended in a transient database failure and the whole operation began again.
    /// </summary>
    /// <remarks>
    /// What a retry looks like from outside: the transaction from the failed attempt is gone, and
    /// the next entry is another <see cref="Begun"/>. It is in the journal rather than counted
    /// separately so the ordering — that nothing was committed in between — is what an assertion
    /// reads.
    /// </remarks>
    public const string RetriedAfterFailure = "retried after a transient failure";

    private readonly List<string> _entries = [];

    /// <summary>What happened, oldest first.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Records that something happened.</summary>
    public void Record(string entry) => _entries.Add(entry);
}
