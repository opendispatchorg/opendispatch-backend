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

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(AuthClaimTypes.Org, user.OrgId.Value.ToString()),
            new(AuthClaimTypes.Role, user.Role.ToString()),
        };

        // Only a technician's own login carries one — see AuthUser.TechnicianId's remarks.
        if (user.TechnicianId is { } technician)
        {
            claims.Add(new Claim(AuthClaimTypes.Technician, technician.Value.ToString()));
        }

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
