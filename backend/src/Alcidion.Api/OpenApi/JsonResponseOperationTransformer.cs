using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Alcidion.Api.OpenApi;

/// <summary>Removes default formatter media types that the API's JSON responses do not use.</summary>
public sealed class JsonResponseOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // Produces("application/json") also runs as an MVC result filter: it overwrites the
        // application/problem+json content type set by Problem()/ValidationProblem(). Limit
        // this to the document so describing JSON does not change existing error responses.
        if (operation.Responses is null) return Task.CompletedTask;
        foreach (var response in operation.Responses.Values)
        {
            if (response.Content is not { } content) continue;
            foreach (var mediaType in content.Keys.Where(type => type is not ("application/json" or "application/problem+json")).ToArray())
                content.Remove(mediaType);
        }
        return Task.CompletedTask;
    }
}
