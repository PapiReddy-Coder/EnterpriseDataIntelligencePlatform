using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Infrastructure;

public static class AnalyticsResponses
{
    public static bool IsAnalytics(PathString path) =>
        path.StartsWithSegments("/api/analytics") || path.StartsWithSegments("/api/reports") ||
        path.StartsWithSegments("/api/lineage");

    public static AnalyticsMetadata Metadata(HttpContext context, object? filter = null)
    {
        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in context.Request.Query) filters[item.Key] = item.Value.ToString();
        if (filter is not null)
            foreach (var item in JsonSerializer.SerializeToElement(filter, filter.GetType(),
                         new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject()
                         .Where(x => x.Value.ValueKind != JsonValueKind.Null))
                filters[item.Name] = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString()! : item.Value.GetRawText();
        return new(DateTime.UtcNow, "UTC", context.TraceIdentifier, filters);
    }

    public static AnalyticsResponse<T> Success<T>(HttpContext context, T data, object? filter = null,
        AnalyticsPagination? page = null) => new(true, data, Metadata(context, filter), page, null);

    public static AnalyticsResponse<object> Failure(HttpContext context, string code, string message,
        IReadOnlyList<string>? details = null) => new(false, null, Metadata(context), null, new(code, message, details));
}
