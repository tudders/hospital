using System.Net.Http.Json;
using System.Text.Json;

namespace Alcidion.Api.Tests;

/// <summary>
/// Reads the per-field <c>errors</c> dictionary off a validation failure, asserting it is there.
/// </summary>
/// <remarks>
/// Shared by every endpoint's schema tests on purpose: the point of schema-first validation is that
/// all of them answer in one shape, so all of them are read through one helper. A 400 that needs
/// its own reader is the regression.
/// </remarks>
internal static class ValidationProblem
{
    public static async Task<Dictionary<string, string[]>> FieldErrors(this HttpResponseMessage res)
    {
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("errors", out var errors),
            $"expected a per-field 'errors' dictionary, got: {body}");
        return errors.Deserialize<Dictionary<string, string[]>>()!;
    }

    /// <summary>Every message in the response, across all fields.</summary>
    public static async Task<IReadOnlyList<string>> Messages(this HttpResponseMessage res) =>
        (await res.FieldErrors()).Values.SelectMany(m => m).ToList();
}
