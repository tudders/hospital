namespace Alcidion.Api.Observability;

/// <summary>
/// Keeps patient-identifying query values out of the telemetry the API emits about itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/patients?search=</c> carries a patient's name, and the request URL is repeated into
/// places nobody is thinking about PHI when they configure: the <c>url.query</c> attribute on every
/// ASP.NET Core span, and whatever the reverse proxy writes for <c>cs-uri-query</c>. The decision to
/// keep the search a GET is recorded in docs/adr/0004-patient-search-stays-a-get.md; this is the half
/// of that decision the API can enforce for itself.
/// </para>
/// <para>
/// Redaction is by parameter name, and the name is kept. Dropping the query wholesale would cost the
/// one thing the attribute is read for - telling a search apart from a list - which is exactly the
/// trade that gets a redaction turned off again.
/// </para>
/// </remarks>
public static class QueryRedaction
{
    public const string Placeholder = "REDACTED";

    /// <summary>
    /// Query parameters whose values may identify a patient. Anything added to a controller's
    /// <c>[FromQuery]</c> surface that can carry a name, an MRN or a date of birth belongs here.
    /// </summary>
    private static readonly string[] Sensitive = ["search"];

    /// <summary>
    /// The query string with every sensitive value replaced, or <paramref name="query"/> unchanged
    /// when it carries none. Works on the raw text rather than a parsed collection: what reaches the
    /// span is the raw query, and re-encoding a parsed one would quietly rewrite queries that are
    /// not the point of this.
    /// </summary>
    public static string Redact(string? query)
    {
        if (string.IsNullOrEmpty(query)) return "";

        var start = query[0] == '?' ? 1 : 0;
        var pairs = query[start..].Split('&');
        var changed = false;

        for (var i = 0; i < pairs.Length; i++)
        {
            var separator = pairs[i].IndexOf('=');
            if (separator < 0) continue;

            var name = pairs[i][..separator];
            if (!Sensitive.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

            pairs[i] = $"{name}={Placeholder}";
            changed = true;
        }

        return changed ? query[..start] + string.Join('&', pairs) : query;
    }
}
