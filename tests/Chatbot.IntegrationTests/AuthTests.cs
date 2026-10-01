using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Chatbot.Application.DTOs;
using Chatbot.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.IntegrationTests;

public sealed class AuthTests : IClassFixture<ChatbotApiFactory>
{
    private readonly ChatbotApiFactory _factory;

    public AuthTests(ChatbotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_ReturnsCreatedWithToken_AndTokenWorksForMe()
    {
        var client = _factory.CreateClient();
        var email = $"new-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "passw0rd!", "New User"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.Equal("New User", auth.User.DisplayName);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Register_InvalidInput_ReturnsValidationProblem()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest("bad", "short", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Email", problem!.Errors.Keys);
        Assert.Contains("Password", problem.Errors.Keys);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(email);

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "passw0rd!", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem!.Status);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var client = _factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(email, "passw0rd!");

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "passw0rd!"));

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(email, auth!.User.Email);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorizedProblem()
    {
        var client = _factory.CreateClient();
        var email = $"wrong-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(email, "passw0rd!");

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "not-the-password1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid email or password.", problem!.Detail);
    }

    [Theory]
    [InlineData("GET", "/api/conversations")]
    [InlineData("POST", "/api/conversations")]
    [InlineData("POST", "/api/chat")]
    [InlineData("GET", "/api/auth/me")]
    public async Task PrivateEndpoints_WithoutToken_Return401(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { message = "hi" });
        }

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PrivateEndpoint_WithTamperedToken_Returns401()
    {
        var client = _factory.CreateClient();
        var auth = await client.RegisterAsync();
        var tampered = auth.AccessToken[..^4] + (auth.AccessToken.EndsWith("AAAA") ? "BBBB" : "AAAA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await client.GetAsync("/api/conversations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
