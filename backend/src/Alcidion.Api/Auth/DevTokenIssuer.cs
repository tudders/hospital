using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Alcidion.Api.Auth;

/// <summary>
/// Issues signed JWTs for a hard-coded set of demo users. Stands in for a real identity provider.
/// </summary>
public sealed class DevTokenIssuer(JwtOptions options, TimeProvider time, DemoUsers demoUsers)
{
    private static readonly Dictionary<string, (string Password, string[] Roles)> Users = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nurse"] = ("nurse", [Roles.Clinician]),
        ["doctor"] = ("doctor", [Roles.Clinician]),
        ["admin"] = ("admin", [Roles.Admin]),
        ["viewer"] = ("viewer", []),
    };

    public string? Issue(string username, string password)
    {
        // Checked here as well as at startup: the credentials are a constant in this assembly, so
        // the only thing that can stop them is the check standing between them and a signature.
        if (!demoUsers.Enabled) return null;
        if (!Users.TryGetValue(username, out var user) || user.Password != password) return null;

        var now = time.GetUtcNow();
        var claims = new List<Claim> { new(ClaimTypes.Name, username.ToLowerInvariant()), new(JwtRegisteredClaimNames.Sub, username.ToLowerInvariant()) };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(options.LifetimeMinutes).UtcDateTime,
            signingCredentials: new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
