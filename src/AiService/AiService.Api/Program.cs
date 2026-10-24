using AiService.Api.Infrastructure;
using AiService.Api.Mcp;
using AiService.Business;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Single entry into the layer stack: AddBusiness chains down to AddDataAccess.
// The Api never registers a DbContext or repository directly.
builder.Services.AddBusiness(builder.Configuration);

builder.Services.AddAiRateLimiting(builder.Configuration);

// MCP server at /mcp, serving the assistant's tools to outside clients.
// Stateless: every request stands alone, so there is no per-client session to keep in
// memory, and any replica can answer. Fine here because every tool is a one-shot read.
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<DealMcpTools>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Please enter a valid JWT token"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

var app = builder.Build();

// Apply migrations and seed the prompt templates before serving traffic.
await app.Services.InitializeDatabaseAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// After authorization, not before: the limiter partitions on the "sub" claim, so the
// principal has to exist by the time it runs. It also means an unauthenticated caller is
// already a 401 and never consumes anyone's budget.
app.UseAiRateLimiter();

app.MapControllers();

// Same login rule as the controllers: no valid PropTrack JWT, no MCP.
app.MapMcp("/mcp").RequireAuthorization();

app.Run();
