using System.Collections.Concurrent;
using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;

namespace Chatbot.IntegrationTests.Infrastructure;

/// <summary>Deterministic stand-in for the AI provider. Replies "Echo: {last user message}" in chunks.</summary>
public sealed class FakeAiChatService : IAiChatService
{
    private readonly ConcurrentQueue<Exception> _failures = new();

    public ConcurrentBag<IReadOnlyList<AiChatMessage>> Calls { get; } = [];

    /// <summary>The next call throws <paramref name="exception"/> instead of replying.</summary>
    public void FailNextWith(Exception exception) => _failures.Enqueue(exception);

    public async Task<AiChatResult> GenerateResponseAsync(
        IReadOnlyList<AiChatMessage> history,
        Func<string, CancellationToken, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        Calls.Add(history);

        if (_failures.TryDequeue(out var failure))
        {
            throw failure;
        }

        var reply = "Echo: " + history[^1].Content;
        if (onDelta is not null)
        {
            foreach (var chunk in reply.Chunk(4))
            {
                await onDelta(new string(chunk), cancellationToken);
            }
        }

        return new AiChatResult(reply, "fake-model", history.Count, reply.Length, "end_turn");
    }

    public static AiServiceException Unavailable() => new("The AI service is temporarily unavailable. Please try again.", isTransient: true);
}
