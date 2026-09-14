# 5. HTTPS is enforced at the host

Date: 2026-09-14

## Status

Accepted for the IIS / SmarterASP.NET deployment target.

## Context

The API returns patient information and accepts bearer tokens. Item 6 of the API hygiene run
identified that `Program.cs` installs neither HTTPS redirection nor HSTS. The deployment target
is IIS 10, which owns the public TLS binding; local development uses HTTP for the API and Vite.

Redirecting an API call cannot undo sending its token or body over HTTP. Some clients do not
follow redirects, and redirects can break CORS preflight requests. HSTS helps browsers after
they have received the policy over HTTPS, but does not enforce transport for every API client.
These distinctions follow [Microsoft's API HTTPS guidance](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0#api-projects).

## Decision

IIS owns the public HTTPS policy. A deployment must:

- Bind the API hostname to HTTPS with a valid certificate.
- Refuse public HTTP API traffic, either by removing the HTTP binding or requiring SSL at
  the site. HTTP requests must not reach the application or be handled as clinical operations.
- Enable HSTS on HTTPS responses at the host, initially with `max-age=31536000` and without
  `includeSubDomains` or preload unless every affected hostname has been checked.
- Configure clients with the HTTPS API URL from their first request. A redirect is not a
  substitute for that configuration.

`Program.cs` deliberately omits `UseHttpsRedirection()` and `UseHsts()`. Local Development and
Testing retain HTTP. The internal application listener must not be publicly accessible outside
the IIS boundary. A standalone public Kestrel deployment needs an HTTPS-only listener and a
review of this decision before it is exposed.

## Consequences and verification

Transport configuration remains a deployment responsibility, alongside credentials and the IIS
query-log settings in ADR 0004. Startup guards cannot inspect the public IIS bindings, and unit
tests cannot establish that a deployed hostname is HTTPS-only.

For each deployment, verify that an anonymous HTTPS request to `/health` succeeds and carries
`Strict-Transport-Security`; an HTTP request to `/health` must be refused by the host or fail to
connect. Do not test the HTTP boundary with real tokens or patient data. Then verify browser
login and an authenticated read using the HTTPS API URL.

This change records the required configuration; it does not assert that a remote IIS site has
already been configured or verified.
