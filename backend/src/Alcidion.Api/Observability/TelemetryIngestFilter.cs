using System.Globalization;
using System.Threading.RateLimiting;
using Alcidion.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Alcidion.Api.Observability;

/// <summary>Bounds telemetry requests before parsing and limits each validated browser session.</summary>
/// <remarks>
/// A singleton owns the thread-safe limiter; partitions expire when idle. The action filter runs
/// after schema validation so the session key comes from the body, including for sendBeacon, which
/// cannot set custom headers. This is an anonymous, per-process quota, not an authenticated identity.
/// </remarks>
public sealed class TelemetryIngestFilter : IAsyncResourceFilter, IActionFilter, IDisposable
{
    public const int MaxRequestBytes = 64 * 1024;
    public const int BatchesPerMinute = 30;

    private readonly PartitionedRateLimiter<string> _sessions = PartitionedRateLimiter.Create<string, string>(
        session => RateLimitPartition.GetFixedWindowLimiter(session, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = BatchesPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }), StringComparer.Ordinal);

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // The attribute enforces the streaming limit in Kestrel/IIS. Reject a known oversized body
        // without reading it as well, including on hosts such as TestServer that lack that feature.
        if (context.HttpContext.Request.ContentLength > MaxRequestBytes)
        {
            context.Result = TooLarge();
            return;
        }

        var executed = await next();
        // A chunked body can cross the server's limit during model binding. Preserve its 413
        // instead of letting the general exception handler turn that expected rejection into 500.
        if (executed.Exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            executed.ExceptionHandled = true;
            executed.Result = TooLarge();
        }
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var body = (ClientEventBatch)context.ActionArguments["body"]!;
        var session = (string)body[0].SessionId;
        for (var i = 1; i < body.GetArrayLength(); i++)
        {
            if ((string)body[i].SessionId != session)
                context.ModelState.AddModelError($"[{i}].sessionId", "All events in a batch must belong to the same session.");
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));
            return;
        }

        using var lease = _sessions.AttemptAcquire(session);
        if (lease.IsAcquired) return;

        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "The telemetry session's batch limit has been reached.",
        }) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static ObjectResult TooLarge() => new(new ProblemDetails
    {
        Status = StatusCodes.Status413PayloadTooLarge,
        Title = "Telemetry batches must not exceed 64 KiB.",
    }) { StatusCode = StatusCodes.Status413PayloadTooLarge };

    public void Dispose() => _sessions.Dispose();
}
