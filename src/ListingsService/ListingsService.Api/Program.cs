using ListingsService.Business;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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

app.MapControllers();

app.Run();
