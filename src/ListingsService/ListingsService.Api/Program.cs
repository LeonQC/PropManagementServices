using ListingsService.Application.Interfaces;
using ListingsService.Application.Services;
using ListingsService.Infrastructure.Data;
using ListingsService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddDbContext<ListingsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ListingsDb"))
           .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<IPropertyRepository, PropertyRepository>();
builder.Services.AddScoped<PropertyService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
