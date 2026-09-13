using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Publishes the JSON Schema a request type was generated from, in place of what reflection makes
/// of the generated struct.
/// </summary>
/// <remarks>
/// <para>
/// Without this the document is worse than it was before schema-first: the generated types are
/// <c>readonly struct</c>s over a JSON element, so reflection sees no constraints, no property
/// types, and one <c>valueKind</c> member that is not part of the contract at all. The schema
/// document is the contract, so the document is what gets published.
/// </para>
/// <para>
/// Clearing the reflected members is also what keeps <c>MrnEntity</c> and friends out of
/// <c>components.schemas</c>: ASP.NET lifts sub-schemas into components after the transformers run,
/// and there are none left to lift.
/// </para>
/// </remarks>
public sealed class JsonSchemaOpenApiTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (SchemaDocuments.For(context.JsonTypeInfo.Type) is { } document)
        {
            Apply(document, schema);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Copies the keywords this codebase's schemas use. Anything else is left out rather than
    /// guessed at: a keyword that silently failed to publish would be worse than one that is
    /// visibly absent, so add it here when a schema starts using it.
    /// </summary>
    private static void Apply(JsonElement json, OpenApiSchema schema)
    {
        schema.Properties.Clear();
        schema.Required.Clear();
        schema.AdditionalPropertiesAllowed = true;

        foreach (var keyword in json.EnumerateObject())
        {
            switch (keyword.Name)
            {
                case "type": schema.Type = keyword.Value.GetString(); break;
                case "format": schema.Format = keyword.Value.GetString(); break;
                case "pattern": schema.Pattern = keyword.Value.GetString(); break;
                case "title": schema.Title = keyword.Value.GetString(); break;
                case "description": schema.Description = keyword.Value.GetString(); break;
                case "minLength": schema.MinLength = keyword.Value.GetInt32(); break;
                case "maxLength": schema.MaxLength = keyword.Value.GetInt32(); break;
                case "minItems": schema.MinItems = keyword.Value.GetInt32(); break;
                case "maxItems": schema.MaxItems = keyword.Value.GetInt32(); break;
                case "minimum": schema.Minimum = keyword.Value.GetDecimal(); break;
                case "maximum": schema.Maximum = keyword.Value.GetDecimal(); break;
                case "default": schema.Default = Any(keyword.Value); break;

                case "required":
                    foreach (var name in keyword.Value.EnumerateArray()) schema.Required.Add(name.GetString()!);
                    break;

                case "properties":
                    foreach (var property in keyword.Value.EnumerateObject())
                    {
                        var child = new OpenApiSchema();
                        Apply(property.Value, child);
                        schema.Properties[property.Name] = child;
                    }
                    break;

                case "items":
                    schema.Items = new OpenApiSchema();
                    Apply(keyword.Value, schema.Items);
                    break;

                case "additionalProperties" when keyword.Value.ValueKind is JsonValueKind.False:
                    schema.AdditionalPropertiesAllowed = false;
                    break;
            }
        }
    }

    private static IOpenApiAny? Any(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new OpenApiString(value.GetString()),
        JsonValueKind.Number => new OpenApiDouble(value.GetDouble()),
        JsonValueKind.True or JsonValueKind.False => new OpenApiBoolean(value.GetBoolean()),
        _ => null,
    };
}

/// <summary>
/// The schema documents, read back at runtime from the assembly they were compiled into.
/// </summary>
/// <remarks>
/// The source generator consumes them at build time as <c>AdditionalFiles</c>; publishing them
/// needs the same bytes at run time, and embedding is what keeps the two from drifting apart or
/// depending on a file that only exists in the source tree.
/// </remarks>
internal static class SchemaDocuments
{
    private static readonly ConcurrentDictionary<Type, JsonDocument?> Cache = new();

    /// <summary>The schema a type was generated from, or <see langword="null"/> if it was not.</summary>
    public static JsonElement? For(Type type) =>
        Cache.GetOrAdd(type, static t => Load(t))?.RootElement;

    private static JsonDocument? Load(Type type)
    {
        // The generator's own attribute is internal to this assembly, so it is matched by name.
        var attribute = type.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "JsonSchemaTypeGeneratorAttribute");

        if (attribute?.GetType().GetProperty("Location")?.GetValue(attribute) is not string location) return null;

        var name = location[(location.LastIndexOfAny(['/', '\\']) + 1)..];
        using var stream = typeof(SchemaDocuments).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"'{name}' is generated into {type.Name} but not embedded. Add it to <EmbeddedResource> in Alcidion.Api.Contracts.csproj.");

        return JsonDocument.Parse(stream);
    }
}
