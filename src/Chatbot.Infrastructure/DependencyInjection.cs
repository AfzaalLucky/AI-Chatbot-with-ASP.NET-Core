using Anthropic;
using Chatbot.Application.Abstractions;
using Chatbot.Infrastructure.Ai;
using Chatbot.Infrastructure.Persistence;
using Chatbot.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        AddSecurity(services, configuration);
        AddAi(services, configuration);
        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        var provider = configuration["Database:Provider"] ?? "SqlServer";

        services.AddDbContext<ChatbotDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                // Lightweight option for local demos and integration tests.
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 3));
            }
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ChatbotDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
    }

    private static void AddSecurity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
    }

    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One client for the app lifetime: it owns a pooled HttpClient and is thread-safe.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
                ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                : options.ApiKey;

            return string.IsNullOrWhiteSpace(apiKey)
                ? new AnthropicClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = options.MaxRetries }
                : new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = options.MaxRetries };
        });

        services.AddScoped<IAiChatService, AnthropicChatService>();
    }
}
