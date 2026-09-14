using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Corvus.Text.Json;
using Corvus.Text.Json.Internal;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Binds a <c>[FromBody]</c> parameter whose type was generated from a JSON Schema, and evaluates
/// the body against that schema before the action runs.
/// </summary>
/// <remarks>
/// <para>
/// Failures go into <c>ModelState</c> keyed by the field the caller sent, so <c>[ApiController]</c>
/// turns them into its usual 400 with an <c>errors</c> dictionary. Nothing here constructs a
/// response: the shape stays the one the frontend already parses.
/// </para>
/// <para>
/// The generated types are <c>readonly struct</c>s over a pooled document, so the document is
/// registered for disposal with the response rather than disposed here - the struct is only a view
/// over it, and the action still has to read it.
/// </para>
/// </remarks>
public sealed class JsonSchemaInputFormatter : InputFormatter
{
    private delegate Task<InputFormatterResult> Reader(InputFormatterContext context);

    private static readonly ConcurrentDictionary<Type, Reader?> Readers = new();

    private static readonly MethodInfo ReadGeneric =
        typeof(JsonSchemaInputFormatter).GetMethod(nameof(ReadAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public JsonSchemaInputFormatter()
    {
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
    }

    /// <summary>True for the types the Corvus source generator produced, and nothing else: every
    /// other body type keeps falling through to the System.Text.Json formatter.</summary>
    public static bool IsSchemaGenerated(Type type) =>
        type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IJsonElement<>) &&
            i.GenericTypeArguments[0] == type);

    protected override bool CanReadType(Type type) => ReaderFor(type) is not null;

    public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context) =>
        ReaderFor(context.ModelType)!(context);

    private static Reader? ReaderFor(Type modelType) =>
        Readers.GetOrAdd(modelType, static t =>
            IsSchemaGenerated(t) ? ReadGeneric.MakeGenericMethod(t).CreateDelegate<Reader>() : null);

    private static async Task<InputFormatterResult> ReadAsync<T>(InputFormatterContext context)
        where T : struct, IJsonElement<T>
    {
        var http = context.HttpContext;

        ParsedJsonDocument<T> document;
        try
        {
            document = await ParsedJsonDocument<T>.ParseAsync(http.Request.Body, default, http.RequestAborted);
        }
        catch (JsonException)
        {
            return NotJson(context);
        }
        catch (FormatException)
        {
            return NotJson(context);
        }

        http.Response.RegisterForDispose(document);

        T model = document.RootElement;

        // Basic reports only that the body did not match; Detailed is what names the keyword that
        // failed, and the messages below are built from that.
        using var results = JsonSchemaResultsCollector.Create(JsonSchemaResultsLevel.Detailed);
        if (model.EvaluateSchema(results))
        {
            return InputFormatterResult.Success(model);
        }

        AddFieldErrors(results, context.ModelState);
        return InputFormatterResult.Failure();
    }

    /// <summary>A body that is not JSON at all has no field to blame, so the message goes under the
    /// empty key, where <c>[ApiController]</c> renders it as a general error.</summary>
    private static InputFormatterResult NotJson(InputFormatterContext context)
    {
        context.ModelState.TryAddModelError(string.Empty, "The request body is not valid JSON.");
        return InputFormatterResult.Failure();
    }

    /// <summary>
    /// Turns the collector's results into per-field ModelState errors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document evaluation location is a JSON Pointer into the body, which
    /// <see cref="FieldKey"/> turns into the key the frontend binds its inputs to.
    /// </para>
    /// <para>
    /// Each failing field also produces a result for its subschema as a whole, saying only that it
    /// did not match. That is noise next to the keyword results that say why, so it is held back
    /// and used only if nothing more specific failed at or below that field.
    /// </para>
    /// </remarks>
    private static void AddFieldErrors(JsonSchemaResultsCollector results, ModelStateDictionary modelState)
    {
        Dictionary<string, List<string>> specific = new(StringComparer.Ordinal);
        Dictionary<string, string> subschemaOnly = new(StringComparer.Ordinal);
        string? rootMessage = null;

        foreach (var result in results.EnumerateResults())
        {
            if (result.IsMatch) continue;

            var field = FieldKey(result.GetDocumentEvaluationLocationText());
            if (field.Length == 0)
            {
                rootMessage ??= result.GetMessageText();
                continue;
            }

            var schemaLocation = result.GetSchemaEvaluationLocationText();
            if (IsSubschema(schemaLocation))
            {
                subschemaOnly.TryAdd(field, result.GetMessageText());
                continue;
            }

            if (!specific.TryGetValue(field, out var messages))
            {
                specific[field] = messages = [];
            }

            messages.Add(Describe(field, Keyword(schemaLocation), result.GetMessageText()));
        }

        foreach (var (field, messages) in specific)
        {
            foreach (var message in messages) modelState.TryAddModelError(field, message);
        }

        foreach (var (field, message) in subschemaOnly)
        {
            // "did not match the schema" filed under [0] says nothing the message under [0].name
            // has not already said, so it is only worth emitting if nothing below it failed.
            if (!specific.Keys.Any(k => IsAtOrUnder(k, field))) modelState.TryAddModelError(field, message);
        }

        // A collector that reports nothing still has to produce a 400 rather than a silent success.
        if (specific.Count == 0 && subschemaOnly.Count == 0)
        {
            modelState.TryAddModelError(string.Empty, rootMessage ?? "The request body does not match the schema.");
        }
    }

    /// <summary>
    /// Corvus writes an accurate message for every keyword, and most are fit to show a caller as
    /// they are - they state the bound that was broken. Two are reworded: <c>pattern</c>, which
    /// otherwise quotes the regular expression at the caller, and <c>required</c>, which otherwise
    /// repeats the field name inside a message already filed under it.
    /// </summary>
    private static string Describe(string field, ReadOnlySpan<char> keyword, string corvusMessage) => keyword switch
    {
        "pattern" => $"The {field} field is not in the expected format.",
        "required" => $"The {field} field is required.",
        _ => corvusMessage,
    };

    /// <summary>The trailing segment of a schema evaluation location, which is the keyword that
    /// failed: <c>/properties/mrn/pattern</c> gives <c>pattern</c>.</summary>
    private static ReadOnlySpan<char> Keyword(string schemaLocation) =>
        schemaLocation.AsSpan()[(schemaLocation.LastIndexOf('/') + 1)..];

    /// <summary>
    /// True for a location naming a subschema rather than a keyword inside it -
    /// <c>/properties/mrn</c> or <c>/items</c>, not <c>/properties/mrn/pattern</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the containers this codebase's schemas use are listed, on the same terms as the OpenAPI
    /// transformer: a missed one costs a redundant message, not a missed error.
    /// </para>
    /// <para>
    /// An empty location is one of them. Corvus reports the schema location relative to the schema
    /// being evaluated, and a property it generates as an entity of its own - "patientId", a string
    /// whose only keyword is a format - is evaluated as a root, so its whole-schema result arrives
    /// with no location at all while the keyword under it arrives as "/format". Reading the empty
    /// one as a keyword result is what used to put "The value was expected to match the subschema."
    /// in front of a caller alongside the message that actually said what was wrong.
    /// </para>
    /// </remarks>
    private static bool IsSubschema(string schemaLocation)
    {
        if (schemaLocation.Length == 0) return true;

        var lastSlash = schemaLocation.LastIndexOf('/');
        if (lastSlash < 0) return false;

        if (schemaLocation.AsSpan()[(lastSlash + 1)..] is "items") return true;
        if (lastSlash == 0) return false;

        var parent = schemaLocation.AsSpan()[..lastSlash];
        return parent[(parent.LastIndexOf('/') + 1)..] is "properties";
    }

    /// <summary>
    /// Turns the JSON Pointer the collector reports into the field key ModelState and the frontend
    /// use: <c>/givenName</c> becomes <c>givenName</c>, <c>/props/sessionId</c> becomes
    /// <c>props.sessionId</c>, and <c>/2/name</c> becomes <c>[2].name</c>. Pointer escapes are
    /// undone on the way - <c>~1</c> is a literal slash and <c>~0</c> a literal tilde.
    /// </summary>
    /// <remarks>
    /// A token of digits is read as an array index. That is the convention ASP.NET's own model
    /// binder uses, and therefore the one the frontend already keys on; an object property whose
    /// name is all digits is indistinguishable from an index here exactly as it is there.
    /// </remarks>
    private static string FieldKey(string pointer)
    {
        // A pointer either is empty (the root) or begins with '/', so the first token is the empty
        // string before that slash and never a field.
        var tokens = pointer.Split('/');
        if (tokens.Length < 2) return string.Empty;

        var key = new StringBuilder(pointer.Length);
        for (var i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (IsIndex(token))
            {
                key.Append('[').Append(token).Append(']');
                continue;
            }

            if (key.Length > 0) key.Append('.');
            key.Append(token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
        }

        return key.ToString();
    }

    private static bool IsIndex(string token) => token.Length > 0 && token.All(char.IsAsciiDigit);

    /// <summary>True if <paramref name="key"/> names <paramref name="field"/> itself or something
    /// inside it: <c>[0]</c> contains <c>[0].name</c> and <c>[0][1]</c>, but not <c>[01]</c>.</summary>
    private static bool IsAtOrUnder(string key, string field) =>
        key.StartsWith(field, StringComparison.Ordinal) &&
        (key.Length == field.Length || key[field.Length] is '.' or '[');
}
