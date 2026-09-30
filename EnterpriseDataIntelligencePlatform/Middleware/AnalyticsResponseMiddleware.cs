using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Analytics;

namespace EnterpriseDataIntelligencePlatform.Middleware;

/// <summary>Task 20 analytics/report and Task 21 lineage routes receive the standard envelope.</summary>
public sealed class AnalyticsResponseMiddleware(RequestDelegate next, ILogger<AnalyticsResponseMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!AnalyticsResponses.IsAnalytics(context.Request.Path)) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            await next(context);
            if (!context.Response.HasStarted && context.Response.StatusCode is 401 or 403 or 404 or 405 or 415)
            {
                var (code, message) = context.Response.StatusCode switch
                {
                    401 => ("Unauthenticated", "Authentication is required."),
                    403 => ("Forbidden", "You do not have permission for this action."),
                    404 => ("NotFound", "The requested resource or endpoint was not found."),
                    405 => ("MethodNotAllowed", "This HTTP method is not supported for the endpoint."),
                    _ => ("UnsupportedMediaType", "The request content type is not supported.")
                };
                await context.Response.WriteAsJsonAsync(AnalyticsResponses.Failure(context,
                    code, message));
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (AnalyticsRequestException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(AnalyticsResponses.Failure(context, ex.Code, ex.Message));
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Analytics or lineage request failed. TraceId: {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(AnalyticsResponses.Failure(context, "InternalError",
                "An unexpected error occurred. Contact support with the trace ID."));
        }
    }
}
