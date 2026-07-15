using DocumentsService.Business;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Single entry into the layer stack: AddBusiness chains down to AddDataAccess.
// The Api never registers a DbContext, repository, or storage client directly.
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

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

var app = builder.Build();

// Apply migrations and make sure the blob bucket exists before serving traffic.
// No seed step — documents start empty by design.
await app.Services.InitializeDatabaseAsync();
await app.Services.EnsureStorageAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
