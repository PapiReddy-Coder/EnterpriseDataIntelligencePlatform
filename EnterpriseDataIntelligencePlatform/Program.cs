using EnterpriseDataIntelligencePlatform.Data.Seed;
using EnterpriseDataIntelligencePlatform.Extensions;
using EnterpriseDataIntelligencePlatform.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();

// The schema must exist before Identity queries are executed.
await app.ApplyDatabaseMigrationsAsync();
await AdminUserSeed.SeedAsync(app.Services);

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

public partial class Program;
