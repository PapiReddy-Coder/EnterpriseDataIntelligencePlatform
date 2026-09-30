using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EnterpriseDataIntelligencePlatform.Infrastructure;

public sealed class AnalyticsOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AnalyticsController) &&
            context.MethodInfo.DeclaringType != typeof(ReportsController) &&
            context.MethodInfo.DeclaringType != typeof(LineageController)) return;
        foreach (var code in new[] { "400", "401", "403", "404", "409", "413", "500" })
        {
            if (!operation.Responses.TryGetValue(code, out var response))
            {
                if (code != "500") continue;
                response = new OpenApiResponse { Description = "Unexpected error; response contains a trace ID." };
                operation.Responses[code] = response;
            }
            response.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = context.SchemaGenerator.GenerateSchema(typeof(AnalyticsResponse<object>), context.SchemaRepository)
                }
            };
        }
        if (context.MethodInfo.Name == nameof(ReportsController.Export))
            operation.Responses["200"] = new OpenApiResponse
            {
                Description = "Download the filtered CSV/XLSX report. X-Report-Row-Count, X-Report-Total-Count, X-Report-Scope and X-Report-Timezone describe the export.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["text/csv"] = new() { Schema = new OpenApiSchema { Type = "string", Format = "binary" } },
                    ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] =
                        new() { Schema = new OpenApiSchema { Type = "string", Format = "binary" } }
                }
            };
    }
}
