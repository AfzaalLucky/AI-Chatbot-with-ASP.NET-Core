using Chatbot.Domain.Enums;

namespace Chatbot.Application.Abstractions;

public sealed record AiChatMessage(MessageRole Role, string Content);

public sealed record AiChatResult(
    string Content,
    string Model,
    int? InputTokens,
    int? OutputTokens,
    string? StopReason);

/// <summary>
/// Provider-agnostic abstraction over an LLM. Implementations stream text through <c>onDelta</c>
/// (when supplied) and return the complete response. Provider failures surface as
/// <see cref="Common.Exceptions.AiServiceException"/>.
/// </summary>
public interface IAiChatService
{
    Task<AiChatResult> GenerateResponseAsync(
        IReadOnlyList<AiChatMessage> history,
        Func<string, CancellationToken, Task>? onDelta,
        CancellationToken cancellationToken);
}
