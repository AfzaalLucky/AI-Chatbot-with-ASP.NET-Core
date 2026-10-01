using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Domain.Enums;
using Chatbot.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Chatbot.UnitTests.Ai;

/// <summary>
/// Exercises the real Anthropic SDK against a fake HTTP handler, so request shape, SSE parsing
/// and error mapping are verified without calling the live API.
/// </summary>
public sealed class AnthropicChatServiceTests
{
    private static readonly IReadOnlyList<AiChatMessage> History =
    [
        new(MessageRole.User, "Hello"),
        new(MessageRole.Assistant, "Hi! How can I help?"),
        new(MessageRole.User, "Say hello world")
    ];

    private static AnthropicChatService CreateService(FakeHandler handler, Action<AnthropicOptions>? configure = null)
    {
        var options = new AnthropicOptions { Model = "claude-opus-5-5", MaxTokens = 1024, Effort = "low", SystemPrompt = "Be brief." };
        configure?.Invoke(options);

        var client = new AnthropicClient
        {
            ApiKey = "test-key",
            HttpClient = new HttpClient(handler),
            MaxRetries = 0
        };

        return new AnthropicChatService(client, MsOptions.Create(options), NullLogger<AnthropicChatService>.Instance);
    }

    [Fact]
    public async Task GenerateResponse_StreamsDeltasAndReturnsCompleteResult()
    {
        var handler = FakeHandler.Sse(
            MessageStart("claude-opus-5-5", inputTokens: 25),
            TextBlockStart(),
            TextDelta("Hello"),
            TextDelta(" world"),
            BlockStop(),
            MessageDelta("end_turn", outputTokens: 7),
            MessageStop());
        var deltas = new List<string>();

        var result = await CreateService(handler).GenerateResponseAsync(History, (t, _) => { deltas.Add(t); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(["Hello", " world"], deltas);
        Assert.Equal("Hello world", result.Content);
        Assert.Equal("claude-opus-5-5", result.Model);
        Assert.Equal(25, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
        Assert.Equal("end_turn", result.StopReason);
    }

    [Fact]
    public async Task GenerateResponse_SendsHistorySystemPromptAndFallbackOptIn()
    {
        var handler = FakeHandler.Sse(MessageStart("claude-opus-5-5", 1), TextBlockStart(), TextDelta("ok"), BlockStop(), MessageDelta("end_turn", 1), MessageStop());

        await CreateService(handler).GenerateResponseAsync(History, null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("test-key", request.Headers["x-api-key"]);
        Assert.Contains("server-side-fallback-2026-07-01", request.Headers["anthropic-beta"]);

        using var body = JsonDocument.Parse(request.Body);
        var json = body.RootElement;
        Assert.Equal("claude-opus-5-5", json.GetProperty("model").GetString());
        Assert.Equal(1024, json.GetProperty("max_tokens").GetInt32());
        Assert.True(json.GetProperty("stream").GetBoolean());
        Assert.Equal("default", json.GetProperty("fallbacks").GetString());
        Assert.Equal("low", json.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Contains("Be brief.", json.GetProperty("system").ToString());

        var messages = json.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Contains("Say hello world", messages[2].GetProperty("content").ToString());
    }

    [Fact]
    public async Task GenerateResponse_FallbackDisabled_OmitsBetaAndFallbacks()
    {
        var handler = FakeHandler.Sse(MessageStart("claude-opus-5-5", 1), TextBlockStart(), TextDelta("ok"), BlockStop(), MessageDelta("end_turn", 1), MessageStop());

        await CreateService(handler, o => o.EnableRefusalFallback = false).GenerateResponseAsync(History, null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.ContainsKey("anthropic-beta"));
        using var body = JsonDocument.Parse(request.Body);
        Assert.False(body.RootElement.TryGetProperty("fallbacks", out _));
    }

    [Fact]
    public async Task GenerateResponse_Refusal_AppendsNotice()
    {
        var handler = FakeHandler.Sse(MessageStart("claude-opus-5-5", 3), MessageDelta("refusal", 0), MessageStop());

        var result = await CreateService(handler).GenerateResponseAsync(History, null, CancellationToken.None);

        Assert.Equal("refusal", result.StopReason);
        Assert.Equal(AnthropicChatService.RefusalNotice, result.Content);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData((HttpStatusCode)529, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task GenerateResponse_ProviderError_MapsToAiServiceException(HttpStatusCode status, bool transient)
    {
        var handler = FakeHandler.Error(status);

        var ex = await Assert.ThrowsAsync<AiServiceException>(() =>
            CreateService(handler).GenerateResponseAsync(History, null, CancellationToken.None));

        Assert.Equal(transient, ex.IsTransient);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    [Fact]
    public async Task GenerateResponse_NetworkFailure_MapsToTransientAiServiceException()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<AiServiceException>(() =>
            CreateService(handler).GenerateResponseAsync(History, null, CancellationToken.None));

        Assert.True(ex.IsTransient);
    }

    // ----- SSE helpers -----

    private static string Event(string name, object data) => $"event: {name}\ndata: {JsonSerializer.Serialize(data)}\n\n";

    private static string MessageStart(string model, int inputTokens) => Event("message_start", new
    {
        type = "message_start",
        message = new
        {
            id = "msg_test",
            type = "message",
            role = "assistant",
            model,
            content = Array.Empty<object>(),
            stop_reason = (string?)null,
            stop_sequence = (string?)null,
            usage = new { input_tokens = inputTokens, output_tokens = 1 }
        }
    });

    private static string TextBlockStart() => Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } });

    private static string TextDelta(string text) => Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text } });

    private static string BlockStop() => Event("content_block_stop", new { type = "content_block_stop", index = 0 });

    private static string MessageDelta(string stopReason, int outputTokens) => Event("message_delta", new
    {
        type = "message_delta",
        delta = new { stop_reason = stopReason, stop_sequence = (string?)null },
        usage = new { output_tokens = outputTokens }
    });

    private static string MessageStop() => Event("message_stop", new { type = "message_stop" });

    internal sealed record CapturedRequest(Dictionary<string, string> Headers, string Body);

    internal sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<CapturedRequest> Requests { get; } = [];

        public static FakeHandler Sse(params string[] events) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(events), Encoding.UTF8, "text/event-stream")
        });

        public static FakeHandler Error(HttpStatusCode status) => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"api_error","message":"boom"}}""", Encoding.UTF8, "application/json")
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(headers, body));
            return _respond(request);
        }
    }
}
