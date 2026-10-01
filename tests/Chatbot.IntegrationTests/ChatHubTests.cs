using System.Collections.Concurrent;
using Chatbot.Application.DTOs;
using Chatbot.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chatbot.IntegrationTests;

public sealed class ChatHubTests : IClassFixture<ChatbotApiFactory>
{
    private readonly ChatbotApiFactory _factory;

    public ChatHubTests(ChatbotApiFactory factory) => _factory = factory;

    private HubConnection CreateConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .Build();

    [Fact]
    public async Task SendMessage_StreamsDeltasAndReturnsFinalResponse()
    {
        var (_, auth) = await _factory.CreateAuthenticatedClientAsync();
        await using var connection = CreateConnection(auth.AccessToken);

        var deltas = new ConcurrentQueue<string>();
        Guid? savedConversation = null;
        connection.On<Guid, string, MessageDto>("UserMessageSaved", (id, _, _) => savedConversation = id);
        connection.On<Guid, string>("ReceiveDelta", (_, text) => deltas.Enqueue(text));

        await connection.StartAsync();
        var response = await connection.InvokeAsync<ChatResponse>("SendMessage", new ChatRequest(null, "stream me"));

        Assert.Equal("Echo: stream me", response.AssistantMessage.Content);
        Assert.Equal(response.ConversationId, savedConversation);
        Assert.True(deltas.Count > 1, "Expected the reply to arrive in multiple chunks.");
        Assert.Equal("Echo: stream me", string.Concat(deltas));
    }

    [Fact]
    public async Task SendMessage_InvalidInput_ReturnsHubErrorMessage()
    {
        var (_, auth) = await _factory.CreateAuthenticatedClientAsync();
        await using var connection = CreateConnection(auth.AccessToken);
        await connection.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync<ChatResponse>("SendMessage", new ChatRequest(null, "   ")));

        Assert.Contains("Message must not be empty.", ex.Message);
    }

    [Fact]
    public async Task SendMessage_AiFailure_ReturnsFriendlyHubError()
    {
        var (_, auth) = await _factory.CreateAuthenticatedClientAsync();
        await using var connection = CreateConnection(auth.AccessToken);
        await connection.StartAsync();
        _factory.Ai.FailNextWith(FakeAiChatService.Unavailable());

        var ex = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync<ChatResponse>("SendMessage", new ChatRequest(null, "hello?")));

        Assert.Contains("temporarily unavailable", ex.Message);
    }

    [Fact]
    public async Task Connect_WithoutToken_IsRejected()
    {
        await using var connection = CreateConnection(null);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
    }
}
