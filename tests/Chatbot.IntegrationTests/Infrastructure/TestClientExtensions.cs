using System.Net.Http.Headers;
using System.Net.Http.Json;
using Chatbot.Application.DTOs;

namespace Chatbot.IntegrationTests.Infrastructure;

public static class TestClientExtensions
{
    public static async Task<AuthResponse> RegisterAsync(this HttpClient client, string? email = null, string password = "passw0rd!")
    {
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    /// <summary>Creates a client already authenticated as a freshly registered user.</summary>
    public static async Task<(HttpClient Client, AuthResponse Auth)> CreateAuthenticatedClientAsync(this ChatbotApiFactory factory)
    {
        var client = factory.CreateClient();
        var auth = await client.RegisterAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }
}
