using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Alcidion.Api.Observability;

/// <summary>
/// Custom attribute + action filter. MVC runs filters as a Chain of Responsibility; this one
/// decorates the action it wraps, adding audit logging and timing without changing the action.
/// Marks an action as auditable: logs who did what, with the correlation id, and times it.
/// Usage: [Audited("patient.register")]
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AuditedAttribute(string action) : Attribute, IAsyncActionFilter
{
    public string Action { get; } = action;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<AuditedAttribute>>();
        var user = context.HttpContext.User.Identity?.Name ?? "anonymous";
        var sw = Stopwatch.StartNew();

        using var span = Telemetry.ActivitySource.StartActivity($"audit:{Action}");
        span?.SetTag("audit.action", Action);
        span?.SetTag("audit.user", user);

        var executed = await next();
        sw.Stop();

        var status = executed.Exception is null ? (executed.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode ?? 200 : 500;
        logger.LogInformation("AUDIT {Action} by {User} -> {StatusCode} in {ElapsedMs}ms [corr {CorrelationId}]",
            Action, user, status, sw.ElapsedMilliseconds, context.HttpContext.CorrelationId());
    }
}
