using KameronSushi.Application.Features.WhatsApp;
using KameronSushi.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var renderPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql");
builder.Services.AddScoped<WhatsAppConversationService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

await app.Services.ApplyKameronSushiMigrationsAsync();

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
