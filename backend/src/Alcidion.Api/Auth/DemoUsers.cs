namespace Alcidion.Api.Auth;

/// <summary>
/// Whether <see cref="DevTokenIssuer"/>'s hard-coded credentials may issue tokens. Configured by
/// <c>Auth:AllowDemoUsers</c> and on by default, so development and the integration tests need no
/// configuration at all - which is exactly why <see cref="Configuration.StartupGuards"/> refuses to
/// start anywhere else until someone has turned it off.
/// </summary>
public sealed record DemoUsers(bool Enabled);
