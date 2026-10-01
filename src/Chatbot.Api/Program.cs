using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Chatbot.Api.Hubs;
using Chatbot.Api.Infrastructure;
using Chatbot.Application;
using Chatbot.Infrastructure;
using Chatbot.Infrastructure.Persistence;
using Chatbot.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Request size limits: chat messages are small, so reject oversized bodies early.
var maxBodyBytes = configuration.GetValue<long?>("RequestLimits:MaxRequestBodyBytes") ?? 256 * 1024;
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = maxBodyBytes;
});

// Layers
builder.Services.AddApplication(configuration);
builder.Services.AddInfrastructure(configuration);

// Web API + Problem Details
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Authentication (JWT bearer) and authorization: every endpoint requires a user unless marked [AllowAnonymous].
var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Name
        };
        options.Events = new JwtBearerEvents
        {
            // Browsers cannot set headers on WebSocket/SSE connections, so SignalR sends the token in the query string.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// SignalR
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = configuration.GetValue<long?>("RequestLimits:MaxSignalRMessageBytes") ?? 64 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});

// CORS: only the configured front-end origins may call the API.
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent")
        .WithMethods("GET", "POST", "DELETE", "OPTIONS")
        .AllowCredentials()
        .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
});

builder.Services.AddChatbotRateLimiting(configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<ChatbotDbContext>("database");
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";

    // Kestrel also enforces MaxRequestBodySize (including chunked bodies); this rejects declared
    // oversized bodies up-front with a Problem Details response, whatever server hosts the app.
    if (context.Request.ContentLength > maxBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Request too large",
                Detail = $"Request bodies are limited to {maxBodyBytes} bytes."
            }
        });
        return;
    }

    await next();
});

app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHealthChecks("/health").AllowAnonymous().DisableRateLimiting();

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:MigrateOnStartup", false))
    {
        return;
    }

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ChatbotDbContext>();

    if (db.Database.IsSqlServer())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        // Migrations are generated for SQL Server; other providers get the schema directly from the model.
        await db.Database.EnsureCreatedAsync();
    }
}
