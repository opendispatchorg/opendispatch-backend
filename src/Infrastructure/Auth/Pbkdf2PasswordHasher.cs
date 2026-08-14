using System.Globalization;
using System.Security.Cryptography;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> over PBKDF2 (<see cref="Rfc2898DeriveBytes"/>) — the BCL's own
/// implementation, so this adapter needs no package beyond what .NET already ships.
/// </summary>
/// <remarks>
/// The iteration count travels inside the hash rather than living only in
/// <see cref="Iterations"/>, so raising it later cannot break a password hashed under the old
/// count: <see cref="Verify"/> reads back whatever count a hash was actually made with. A random
/// salt per password is what makes two identical passwords hash differently, and
/// <see cref="CryptographicOperations.FixedTimeEquals"/> is what keeps a failed comparison from
/// finishing sooner the more bytes it gets right.
/// </remarks>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}");
    }

    /// <remarks>
    /// <strong>A hash this method cannot read is a failed verification, not an exception.</strong>
    /// Every part of the stored value is parsed rather than trusted — the iteration count, both
    /// base64 fields, and the key length — because the one caller is the login path, and a row
    /// corrupted by a bad migration or a hand-edited database should refuse the password rather than
    /// answer a 500 that says the server is broken. There is no case where the right answer to
    /// "does this password match?" is a stack trace.
    /// </remarks>
    public bool Verify(string password, string hash)
    {
        var parts = hash?.Split('.') ?? [];

        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1
            || !TryDecode(parts[1], out var salt)
            || !TryDecode(parts[2], out var expected)
            || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Reads one base64 field, or reports that it is not one.</summary>
    private static bool TryDecode(string value, out byte[] decoded)
    {
        var buffer = new byte[((value.Length + 3) / 4) * 3];

        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            decoded = [];

            return false;
        }

        decoded = buffer[..written];

        return true;
    }
}
