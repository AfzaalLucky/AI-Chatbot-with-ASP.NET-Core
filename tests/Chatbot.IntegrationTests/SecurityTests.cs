using System.Net;
using System.Net.Http.Json;
using Chatbot.Application.DTOs;
using Chatbot.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.IntegrationTests;

public sealed class RateLimitingTests : IClassFixture<RateLimitedApiFactory>
{
    private readonly RateLimitedApiFactory _factory;

    public RateLimitingTests(RateLimitedApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Chat_ExceedingPerUserLimit_Returns429Problem()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Limit is 2 per minute in this factory.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, "1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, "2"))).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, "3"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = await limited.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem!.Status);
    }

    [Fact]
    public async Task Login_ExceedingPerIpLimit_Returns429()
    {
        var client = _factory.CreateClient();
        HttpStatusCode last = HttpStatusCode.OK;

        for (var i = 0; i < 6; i++)
        {
            last = (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "passw0rd!"))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}

public sealed class CorsAndHeadersTests : IClassFixture<ChatbotApiFactory>
{
    private readonly ChatbotApiFactory _factory;

    public CorsAndHeadersTests(ChatbotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Preflight_FromAllowedOrigin_IsAccepted()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/conversations");
        request.Headers.Add("Origin", "https://frontend.test");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal("https://frontend.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_GetsNoCorsHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/conversations");
        request.Headers.Add("Origin", "https://evil.test");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
