using System.Diagnostics;

namespace Alcidion.Api.Observability;

/// <summary>
/// Accepts an X-Correlation-Id from the caller (the frontend generates one per session/action),
/// or mints one. It is echoed on the response, attached to the current Activity (so it lands on
/// OpenTelemetry spans) and pushed into a logging scope so every log line in the request carries it.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming) && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("alcidion.correlation_id", correlationId);
        Activity.Current?.AddBaggage("correlation_id", correlationId);

        // A message-template scope is both structured (exporters read CorrelationId) and printable
        // (the console renders "=> CorrelationId:abc"). A Dictionary scope renders as its type name.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await next(context);
        }
    }
}

public static class HttpContextCorrelationExtensions
{
    public static string CorrelationId(this HttpContext ctx) =>
        ctx.Items[CorrelationIdMiddleware.ItemKey] as string ?? "";
}
