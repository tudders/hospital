using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Alcidion.Api.OpenApi;

/// <summary>Publishes authentication requirements from the same metadata used at runtime.</summary>
public sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        // AllowAnonymous overrides authorization inherited from a controller. There is no
        // fallback policy in this host, so endpoints without Authorize also remain public.
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any()) return;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, context.Document)] = [],
        });

        // Authentication/authorization short-circuits before MVC; UseStatusCodePages and
        // AddProblemDetails supply these bodies as application/problem+json.
        var problem = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", Response("A valid bearer token is required.", problem));
        operation.Responses.TryAdd("403", Response("The authenticated user is not permitted to perform this operation.", problem));
    }

    private static OpenApiResponse Response(string description, IOpenApiSchema schema) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/problem+json"] = new() { Schema = schema },
        },
    };
}
