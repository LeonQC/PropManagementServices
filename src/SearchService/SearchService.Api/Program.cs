using SearchService.Business;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddBusiness(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Create the index and alias before the snapshot consumer starts feeding them.
await app.Services.InitializeSearchIndexAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
