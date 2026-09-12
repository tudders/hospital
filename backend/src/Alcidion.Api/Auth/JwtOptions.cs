using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Alcidion.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "alcidion";
    public string Audience { get; init; } = "alcidion-web";
    public string Secret { get; init; } = "";
    public int LifetimeMinutes { get; init; } = 60;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Secret));
}

public static class Roles
{
    public const string Clinician = "clinician";
    public const string Admin = "admin";
}

public static class Policies
{
    public const string Clinician = "Clinician";
    public const string Admin = "Admin";
}
