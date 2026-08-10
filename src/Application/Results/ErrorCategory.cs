namespace OpenDispatch.Application.Results;

/// <summary>
/// What kind of failure this is — the part of an <see cref="Error"/> that decides how it is
/// answered, as opposed to the part that says which failure it was.
/// </summary>
/// <remarks>
/// <para>
/// Categories exist so the edge does not need a table of every error code in the system. Step 46
/// maps a category to an HTTP status once; a new slice inventing a new code inherits the mapping
/// for free, and only a genuinely new *kind* of failure — which should be rare — touches this
/// enum and that map together.
/// </para>
/// <para>
/// There are three, and none of them is "something went wrong". An unexpected failure is an
/// exception: it is not a result the caller was ever going to handle, and modelling it here
/// would invite handlers to catch and downgrade bugs into 400s. Roles and authorisation arrive
/// with auth in step 44 and will add their own category then, when there is something to
/// refuse.
/// </para>
/// </remarks>
public enum ErrorCategory
{
    /// <summary>
    /// The request was malformed: a missing field, an inverted window, a negative quantity.
    /// The caller can fix it and try again. Answered as 400.
    /// </summary>
    Validation,

    /// <summary>
    /// The request was well-formed but names something this tenant does not have. Answered as
    /// 404 — including when the row exists under another organization, because the query
    /// filters mean this tenant genuinely cannot see it and saying otherwise would leak that
    /// it exists.
    /// </summary>
    NotFound,

    /// <summary>
    /// The request was well-formed and the thing exists, but its current state forbids this:
    /// an illegal status transition, a stale version, an invoice already paid. Retrying
    /// unchanged will fail the same way. Answered as 409.
    /// </summary>
    Conflict,
}
