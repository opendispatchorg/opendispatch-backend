namespace OpenDispatch.Domain.Customers;

/// <summary>
/// How to reach a customer.
/// </summary>
/// <remarks>
/// Blank is normalised to null on the way in, so "no phone number" has exactly one
/// representation rather than being sometimes null and sometimes an empty string that came
/// off an untouched form field.
/// </remarks>
public readonly record struct ContactInfo
{
    /// <summary>A customer with no contact details on file at all.</summary>
    public static readonly ContactInfo None = new(null, null);

    /// <summary>Creates contact details, treating blank values as absent.</summary>
    public ContactInfo(string? email, string? phone)
    {
        Email = Normalize(email);
        Phone = Normalize(phone);
    }

    /// <summary>Their email address, or null if there isn't one.</summary>
    public string? Email { get; }

    /// <summary>Their phone number, or null if there isn't one.</summary>
    public string? Phone { get; }

    // Shape is deliberately not checked here. Whether something is a plausible email or a
    // dialable number is a question for request validation at the edge, where it can be
    // reported back to whoever typed it.
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
