using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per minute for every endpoint, per user (or per IP when anonymous).</summary>
    [Range(1, 100_000)]
    public int GlobalPermitsPerMinute { get; set; } = 300;

    /// <summary>Chat messages per minute per user (REST and SignalR combined).</summary>
    [Range(1, 10_000)]
    public int ChatPermitsPerMinute { get; set; } = 20;

    /// <summary>Login/register attempts per minute per IP.</summary>
    [Range(1, 10_000)]
    public int AuthPermitsPerMinute { get; set; } = 10;
}

public static class RateLimitPolicies
{
    public const string Chat = "chat";
    public const string Auth = "auth";
}

/// <summary>
/// Per-user limiter for chat messages. Shared by the REST endpoint (via the rate limiting middleware)
/// and the SignalR hub (which the middleware does not cover), so both paths draw from one budget.
/// </summary>
public sealed class ChatRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public ChatRateLimiter(IOptions<RateLimitingOptions> options)
    {
        var permits = options.Value.ChatPermitsPerMinute;
        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    }

    public RateLimitLease Acquire(string userKey) => _limiter.AttemptAcquire(userKey);

    public void Dispose() => _limiter.Dispose();
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddChatbotRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ChatRateLimiter>();

        services.AddRateLimiter(limiter =>
        {
            var settings = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();

            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.GlobalPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            limiter.AddPolicy(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter("auth:" + ClientIp(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.AuthPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            limiter.AddPolicy(RateLimitPolicies.Chat, new ChatEndpointPolicy());

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Please slow down and try again shortly."
                    }
                });
            };
        });

        return services;
    }

    internal static string PartitionKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? "user:" + context.User.GetUserId()
            : "ip:" + ClientIp(context);

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>Routes REST chat requests through the same <see cref="ChatRateLimiter"/> the hub uses.</summary>
    private sealed class ChatEndpointPolicy : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

        public RateLimitPartition<string> GetPartition(HttpContext httpContext)
        {
            var shared = httpContext.RequestServices.GetRequiredService<ChatRateLimiter>();
            var key = PartitionKey(httpContext);
            return RateLimitPartition.Get(key, k => new SharedLimiterAdapter(shared, k));
        }
    }

    /// <summary>Exposes one partition of the shared limiter as a plain <see cref="RateLimiter"/>.</summary>
    private sealed class SharedLimiterAdapter : RateLimiter
    {
        private readonly ChatRateLimiter _shared;
        private readonly string _key;

        public SharedLimiterAdapter(ChatRateLimiter shared, string key)
        {
            _shared = shared;
            _key = key;
        }

        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) => _shared.Acquire(_key);

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_shared.Acquire(_key));
    }
}
