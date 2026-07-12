using DealsService.Business;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Single entry into the layer stack: AddBusiness chains down to AddDataAccess.
// The Api never registers a DbContext or repository directly.
builder.Services.AddBusiness(builder.Configuration);

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

    // 2. Apply the security requirement globally
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document), // Must exactly match the name defined above
            new List<string>()
        }
    });
});

var app = builder.Build();

// Apply migrations and seed an empty database before serving traffic.
// The seed file ships in the image under Seed/ (see the .csproj content item).
var seedPath = Path.Combine(app.Environment.ContentRootPath, "Seed", "seed-data.sql");
await app.Services.InitializeDatabaseAsync(seedPath);

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
