using DealsService.Business;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Single entry into the layer stack: AddBusiness chains down to AddDataAccess.
// The Api never registers a DbContext or repository directly.
builder.Services.AddBusiness(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
