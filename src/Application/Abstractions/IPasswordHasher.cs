namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Turns a password into something safe to store, and checks a password against what was
/// stored.
/// </summary>
/// <remarks>
/// A port rather than a call to a BCL type directly in the handler, for the same reason
/// <c>IClock</c> is a port over <c>DateTimeOffset.UtcNow</c>: the algorithm is an Infrastructure
/// choice (Document 2 §6), and nothing above it should have to change if that choice ever does.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Produces a hash safe to persist. Never the password itself.</summary>
    string Hash(string password);

    /// <summary>Whether this password is the one <paramref name="hash"/> was made from.</summary>
    bool Verify(string password, string hash);
}
