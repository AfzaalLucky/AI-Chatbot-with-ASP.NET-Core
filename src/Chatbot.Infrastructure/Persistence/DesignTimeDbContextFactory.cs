using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chatbot.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling. Reads the connection string from the
/// <c>ConnectionStrings__DefaultConnection</c> environment variable, falling back to a local SQL Server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChatbotDbContext>
{
    public ChatbotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Data Source=localhost;Initial Catalog=ChatbotDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ChatbotDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ChatbotDbContext(options);
    }
}
