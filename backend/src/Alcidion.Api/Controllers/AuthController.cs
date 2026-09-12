using Alcidion.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, string TokenType = "Bearer");
public sealed record MeResponse(string Name, string[] Roles);

[Route("api/auth")]
public sealed class AuthController(DevTokenIssuer issuer) : ApiController
{
    /// <summary>Demo users: nurse/nurse, doctor/doctor (clinician), admin/admin (admin), viewer/viewer (no role).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request) =>
        issuer.Issue(request.Username, request.Password) is { } token
            ? Ok(new LoginResponse(token))
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");

    [HttpGet("me")]
    [Authorize]
    public ActionResult<MeResponse> Me() =>
        Ok(new MeResponse(
            User.Identity?.Name ?? "",
            User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray()));
}
