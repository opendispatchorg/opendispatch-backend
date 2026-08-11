namespace OpenDispatch.Application.Validation;

/// <summary>
/// How long a piece of free text may be, in one place.
/// </summary>
/// <remarks>
/// <para>
/// These are validation rules and not column widths. The schema stores every one of these as
/// Postgres <c>text</c>, which costs nothing over <c>varchar(n)</c>, and a limit in the database
/// buys only a truncation error arriving from three layers away with no field name attached. A
/// limit here is reported to whoever typed it, keyed by the property it concerns.
/// </para>
/// <para>
/// They live together rather than beside the rules that use them because the same field appears in
/// more than one slice — a name is a name whether it belongs to a customer or a technician — and
/// two slices disagreeing about how long a name may be is a rejection that depends on which screen
/// you used. This file arrived when the second slice needed the first one's number.
/// </para>
/// <para>
/// The numbers are judgment, not fact: long enough that no real trading name, address or number is
/// refused, short enough that a pasted document is.
/// </para>
/// </remarks>
internal static class TextLimits
{
    /// <summary>A person's or business's name.</summary>
    internal const int Name = 200;

    /// <summary>An email address — the length RFC 5321 allows a path.</summary>
    internal const int Email = 320;

    /// <summary>A phone number, with room for an international prefix and an extension.</summary>
    internal const int Phone = 40;

    /// <summary>What a customer calls one of their sites.</summary>
    internal const int Label = 100;

    /// <summary>A postal address, with room for several lines.</summary>
    internal const int Address = 500;

    /// <summary>One skill — "hvac", "gas safe", not a paragraph about it.</summary>
    internal const int Skill = 60;

    /// <summary>A line on an invoice — what was done or fitted, not the story of it.</summary>
    internal const int Description = 200;
}
