using Alcidion.Api.Auth;
using Alcidion.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

/// <summary>An access token issued by the development login endpoint.</summary>
/// <param name="AccessToken">The JWT to send in the Authorization header.</param>
/// <param name="TokenType">The authentication scheme, Bearer.</param>
public sealed record LoginResponse(string AccessToken, string TokenType = "Bearer");
/// <summary>The authenticated user's name and assigned roles.</summary>
public sealed record MeResponse(string Name, string[] Roles);

[Route("api/auth")]
public sealed class AuthController(DevTokenIssuer issuer, DemoUsers demoUsers) : ApiController
{
    /// <summary>Demo users: nurse/nurse, doctor/doctor (clinician), admin/admin (admin), viewer/viewer (no role).</summary>
    [HttpPost("login")]
    [EndpointName("Login")]
    [EndpointSummary("Sign in with a demo account")]
    [EndpointDescription("Exchanges demo credentials for a bearer token. Returns 501 when demo accounts are disabled because no production identity provider is configured.")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request) =>
        // With the demo users switched off there is no issuer behind this route at all. 401 would
        // read as "wrong password" and send the caller looking for a better one.
        !demoUsers.Enabled
            ? Problem(statusCode: StatusCodes.Status501NotImplemented, title: "No identity provider is configured")
            : issuer.Issue((string)request.Username, (string)request.Password) is { } token
                ? Ok(new LoginResponse(token))
                : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");

    [HttpGet("me")]
    [EndpointName("GetCurrentUser")]
    [EndpointSummary("Get the current user")]
    [EndpointDescription("Returns the name and roles carried by the caller's validated bearer token.")]
    [Authorize]
    public ActionResult<MeResponse> Me() =>
        Ok(new MeResponse(
            User.Identity?.Name ?? "",
            User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray()));
}
