using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;

namespace OpenDispatch.Infrastructure.Auth;

/// <summary>
/// <see cref="ITokenIssuer"/> over a symmetrically-signed JWT. The issuing half of Document 2
/// §7's "JWT bearer tokens"; the composition root configures ASP.NET Core's own handler to
/// validate what this writes.
/// </summary>
internal sealed class JwtTokenIssuer(JwtSigningOptions options) : ITokenIssuer
{
    public AuthToken Issue(AuthUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(options.Expiry);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(AuthClaimTypes.Org, user.OrgId.Value.ToString()),
            new Claim(AuthClaimTypes.Role, user.Role.ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AuthToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
