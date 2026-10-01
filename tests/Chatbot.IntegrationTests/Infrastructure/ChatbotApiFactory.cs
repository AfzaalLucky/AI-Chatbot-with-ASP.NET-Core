using Chatbot.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chatbot.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory with a throwaway SQLite database and a fake AI provider,
/// so tests cover the full HTTP pipeline (auth, validation, rate limiting, EF Core) without external services.
/// </summary>
public class ChatbotApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chatbot-tests-{Guid.NewGuid():N}.db");

    public FakeAiChatService Ai { get; } = new();

    protected virtual int ChatPermitsPerMinute => 1000;
    protected virtual int AuthPermitsPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting values are visible while Program.cs builds its services.
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_databasePath}");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:Issuer", "Chatbot.Tests");
        builder.UseSetting("Jwt:Audience", "Chatbot.Tests");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-that-is-long-enough-123");
        builder.UseSetting("Anthropic:ApiKey", "not-used");
        builder.UseSetting("Chat:MaxMessageLength", "500");
        builder.UseSetting("RateLimiting:GlobalPermitsPerMinute", "10000");
        builder.UseSetting("RateLimiting:ChatPermitsPerMinute", ChatPermitsPerMinute.ToString());
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", AuthPermitsPerMinute.ToString());
        builder.UseSetting("RequestLimits:MaxRequestBodyBytes", "4096");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiChatService>();
            services.AddSingleton<IAiChatService>(Ai);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp database.
        }
    }
}

public sealed class RateLimitedApiFactory : ChatbotApiFactory
{
    protected override int ChatPermitsPerMinute => 2;
    protected override int AuthPermitsPerMinute => 3;
}
