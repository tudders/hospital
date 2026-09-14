using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Body of <c>POST /api/auth/login</c>, generated from <c>Schemas/login-request.json</c>.
/// </summary>
/// <remarks>
/// There is no <c>ToCommand</c> here because there is no command: the issuer takes two strings, so
/// the controller casts them at the call site rather than through a wrapper that would only
/// restate the pair. Shape is all this type asserts - wrong credentials are a 401, never a 400,
/// which would tell a caller which half of the pair to keep.
/// </remarks>
[JsonSchemaTypeGenerator("Schemas/login-request.json")]
public readonly partial struct LoginRequest;
