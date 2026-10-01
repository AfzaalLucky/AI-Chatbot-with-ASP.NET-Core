using System.Net;
using System.Net.Http.Json;
using System.Text;
using Chatbot.Application.DTOs;
using Chatbot.Domain.Enums;
using Chatbot.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.IntegrationTests;

public sealed class ChatTests : IClassFixture<ChatbotApiFactory>
{
    private readonly ChatbotApiFactory _factory;

    public ChatTests(ChatbotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PostChat_WithoutConversation_CreatesConversationAndPersistsMessages()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, "What is 2+2?"));

        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.Equal("What is 2+2?", chat!.ConversationTitle);
        Assert.Equal("Echo: What is 2+2?", chat.AssistantMessage.Content);
        Assert.Equal("fake-model", chat.AssistantMessage.Model);

        var detail = await client.GetFromJsonAsync<ConversationDetailDto>($"/api/conversations/{chat.ConversationId}");
        Assert.Equal(["user", "assistant"], detail!.Messages.Select(m => m.Role));
    }

    [Fact]
    public async Task PostChat_ContinuingConversation_SendsHistoryToAi()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var first = await (await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, "unique-first-message")))
            .Content.ReadFromJsonAsync<ChatResponse>();

        await client.PostAsJsonAsync("/api/chat", new ChatRequest(first!.ConversationId, "unique-second-message"));

        var history = _factory.Ai.Calls.Single(h => h[^1].Content == "unique-second-message");
        Assert.Equal(3, history.Count);
        Assert.Equal((MessageRole.User, "unique-first-message"), (history[0].Role, history[0].Content));
        Assert.Equal(MessageRole.Assistant, history[1].Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostChat_EmptyMessage_ReturnsValidationProblem(string message)
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, message));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Message", problem!.Errors.Keys);
    }

    [Fact]
    public async Task PostChat_MessageOverConfiguredLimit_ReturnsValidationProblem()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, new string('a', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_MalformedJson_ReturnsBadRequest()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/chat", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostChat_OversizedBody_IsRejected()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Body limit for tests is 4 KB.
        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, new string('a', 10_000)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_ToAnotherUsersConversation_ReturnsNotFound()
    {
        var (owner, _) = await _factory.CreateAuthenticatedClientAsync();
        var (intruder, _) = await _factory.CreateAuthenticatedClientAsync();
        var chat = await (await owner.PostAsJsonAsync("/api/chat", new ChatRequest(null, "secret")))
            .Content.ReadFromJsonAsync<ChatResponse>();

        var response = await intruder.PostAsJsonAsync("/api/chat", new ChatRequest(chat!.ConversationId, "let me in"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_WhenAiUnavailable_Returns503ProblemAndKeepsUserMessage()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var conversation = await (await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest("x")))
            .Content.ReadFromJsonAsync<ConversationSummaryDto>();
        _factory.Ai.FailNextWith(FakeAiChatService.Unavailable());

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(conversation!.Id, "are you there?"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("The AI service is temporarily unavailable. Please try again.", problem!.Detail);

        var detail = await client.GetFromJsonAsync<ConversationDetailDto>($"/api/conversations/{conversation.Id}");
        var only = Assert.Single(detail!.Messages);
        Assert.Equal("user", only.Role);
    }

    [Fact]
    public async Task PostChat_UnexpectedAiException_Returns500WithoutLeakingDetails()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        _factory.Ai.FailNextWith(new InvalidOperationException("secret internal detail sk-ant-123"));

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(null, "hello"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret internal detail", body);
        Assert.DoesNotContain("sk-ant", body);
    }
}
