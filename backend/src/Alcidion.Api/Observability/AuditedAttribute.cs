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

        // An action that serves more than one clinical operation says which one it turned out to
        // be, so the audit trail still distinguishes a discharge from a transfer.
        var action = context.HttpContext.Items[RefinedAction] as string ?? Action;
        span?.SetTag("audit.action", action);

        var status = executed.Exception is null ? (executed.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode ?? 200 : 500;
        logger.LogInformation("AUDIT {Action} by {User} -> {StatusCode} in {ElapsedMs}ms [corr {CorrelationId}]",
            action, user, status, sw.ElapsedMilliseconds, context.HttpContext.CorrelationId());
    }

    internal const string RefinedAction = "audit.action";
}

public static class AuditRefinement
{
    /// <summary>
    /// Names the operation this request actually performed, for actions that cover more than one.
    /// The audited name is what the trail is read by, so a single endpoint carrying two clinical
    /// transitions has to be able to say which of them it was.
    /// </summary>
    public static void RefineAudit(this HttpContext context, string action) =>
        context.Items[AuditedAttribute.RefinedAction] = action;
}
