using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocumentsService.DataAccess;

/// <summary>
/// Design-time factory used only by the EF Core tools (e.g. `dotnet ef migrations add`).
/// It lets the tooling build a DocumentsDbContext without booting the Api host (and
/// its Kafka/JWT/S3 wiring). Not used at runtime — the app configures the context
/// through AddDataAccess instead.
/// </summary>
public class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DOCUMENTS_DB")
            ?? "Host=localhost;Port=5435;Database=proptrack_documents;Username=proptrack;Password=proptrack";

        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DocumentsDbContext(options);
    }
}
