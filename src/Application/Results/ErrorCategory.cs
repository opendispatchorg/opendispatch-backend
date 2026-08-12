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
/// Four now, and none of them is "something went wrong". An unexpected failure is an
/// exception: it is not a result the caller was ever going to handle, and modelling it here
/// would invite handlers to catch and downgrade bugs into 400s. <see cref="Unauthorized"/> is
/// step 44's addition — the first category with something to refuse before it — and step 46
/// still owns the one place the whole enum turns into a status code.
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

    /// <summary>
    /// The caller's credentials do not check out — an unknown username, a wrong password.
    /// Answered as 401. This is deliberately not what a wrong <em>role</em> gets: that caller
    /// is who they say they are and is refused by an authorization policy before a request
    /// reaches a handler at all, so it never becomes a <see cref="Result"/> for this enum to
    /// describe.
    /// </summary>
    Unauthorized,
}
