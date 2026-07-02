using AuthService.Business;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddBusiness(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations, seed the role set, and create the bootstrap admin on an empty DB.
var adminEmail = app.Configuration["Seed:AdminEmail"] ?? "admin@proptrack.local";
var adminPassword = app.Configuration["Seed:AdminPassword"] ?? "ChangeMe123!";
await app.Services.InitializeDatabaseAsync(adminEmail, adminPassword);

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
