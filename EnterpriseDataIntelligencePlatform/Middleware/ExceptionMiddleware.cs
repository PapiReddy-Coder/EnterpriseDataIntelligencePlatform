using System.Net;
using System.Text.Json;

namespace EnterpriseDataIntelligencePlatform.Middleware;

public sealed class ExceptionMiddleware(
    RequestDelegate next,
    ILogger<ExceptionMiddleware> logger,
    IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled exception. TraceId: {TraceId}",
                context.TraceIdentifier);

            context.Response.StatusCode =
                (int)HttpStatusCode.InternalServerError;

            context.Response.ContentType =
                "application/problem+json";

            var response = new
            {
                title = "An unexpected error occurred.",
                status = 500,
                detail = environment.IsDevelopment()
                    ? exception.InnerException?.Message
                        ?? exception.Message
                    : null,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response));
        }
    }
}