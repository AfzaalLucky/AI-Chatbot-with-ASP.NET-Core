using System.Diagnostics;
using System.Text;
using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.Common.Options;
using Chatbot.Application.DTOs;
using Chatbot.Application.Validation;
using Chatbot.Domain.Entities;
using Chatbot.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Application.Services;

/// <summary>Receives progress while a chat response is being generated (used by SignalR for live streaming).</summary>
public interface IChatStreamObserver
{
    /// <summary>Called once the user's message is persisted, before the AI starts responding.</summary>
    Task OnUserMessageSavedAsync(Guid conversationId, string conversationTitle, MessageDto userMessage, CancellationToken cancellationToken);

    /// <summary>Called for each chunk of generated text.</summary>
    Task OnDeltaAsync(Guid conversationId, string text, CancellationToken cancellationToken);
}

public interface IChatService
{
    Task<ChatResponse> SendMessageAsync(
        Guid userId,
        ChatRequest request,
        IChatStreamObserver? observer,
        CancellationToken cancellationToken);
}

public sealed class ChatService : IChatService
{
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiChatService _ai;
    private readonly ChatOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IConversationRepository conversations,
        IUnitOfWork unitOfWork,
        IAiChatService ai,
        IOptions<ChatOptions> options,
        ILogger<ChatService> logger)
    {
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _ai = ai;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> SendMessageAsync(
        Guid userId,
        ChatRequest request,
        IChatStreamObserver? observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var content = request.Message?.Trim() ?? string.Empty;

        new Validator()
            .Require(content.Length > 0, nameof(request.Message), "Message must not be empty.")
            .Require(content.Length <= _options.MaxMessageLength, nameof(request.Message),
                $"Message must be at most {_options.MaxMessageLength} characters.")
            .ThrowIfInvalid();

        var conversation = await GetOrCreateConversationAsync(userId, request.ConversationId, cancellationToken);

        // Persist the user's message first so it is never lost, even if the AI call fails.
        var userMessage = new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = content
        };
        _conversations.AddMessage(userMessage);
        conversation.EnsureTitleFrom(content);
        conversation.Touch();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (observer is not null)
        {
            await observer.OnUserMessageSavedAsync(conversation.Id, conversation.Title, userMessage.ToDto(), cancellationToken);
        }

        var history = await BuildHistoryAsync(conversation.Id, cancellationToken);

        Func<string, CancellationToken, Task>? onDelta = observer is null
            ? null
            : (text, ct) => observer.OnDeltaAsync(conversation.Id, text, ct);

        var partial = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        AiChatResult result;
        try
        {
            result = await _ai.GenerateResponseAsync(
                history,
                async (text, ct) =>
                {
                    partial.Append(text);
                    if (onDelta is not null)
                    {
                        await onDelta(text, ct);
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client went away mid-stream: keep whatever was generated so history stays coherent.
            if (partial.Length > 0)
            {
                await SaveAssistantMessageAsync(conversation,
                    new AiChatResult(partial.ToString(), "unknown", null, null, "cancelled"),
                    stopwatch.Elapsed, CancellationToken.None);
            }

            throw;
        }
        catch (AiServiceException ex)
        {
            _logger.LogWarning(ex, "AI provider failed for conversation {ConversationId}", conversation.Id);
            throw;
        }

        var assistantMessage = await SaveAssistantMessageAsync(conversation, result, stopwatch.Elapsed, cancellationToken);

        _logger.LogInformation(
            "Assistant replied in conversation {ConversationId} (model {Model}, {InputTokens} in / {OutputTokens} out tokens, {LatencyMs} ms)",
            conversation.Id, result.Model, result.InputTokens, result.OutputTokens, assistantMessage.LatencyMs);

        return new ChatResponse(conversation.Id, conversation.Title, userMessage.ToDto(), assistantMessage.ToDto());
    }

    private async Task<Conversation> GetOrCreateConversationAsync(Guid userId, Guid? conversationId, CancellationToken cancellationToken)
    {
        if (conversationId is { } id)
        {
            // Ownership is enforced by the repository query: other users' conversations look like "not found".
            return await _conversations.GetForUserAsync(id, userId, includeMessages: false, cancellationToken)
                   ?? throw new NotFoundException("Conversation", id);
        }

        var conversation = new Conversation { UserId = userId };
        _conversations.Add(conversation);
        return conversation;
    }

    private async Task<IReadOnlyList<AiChatMessage>> BuildHistoryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var recent = await _conversations.GetRecentMessagesAsync(conversationId, _options.MaxHistoryMessages, cancellationToken);

        // Providers expect the transcript to start with a user turn and to alternate roles.
        // Consecutive turns with the same role (e.g. after a failed AI call) are merged.
        var history = new List<AiChatMessage>();
        foreach (var message in recent.SkipWhile(m => m.Role != MessageRole.User))
        {
            if (history.Count > 0 && history[^1].Role == message.Role)
            {
                history[^1] = history[^1] with { Content = history[^1].Content + "\n\n" + message.Content };
            }
            else
            {
                history.Add(new AiChatMessage(message.Role, message.Content));
            }
        }

        return history;
    }

    private async Task<Message> SaveAssistantMessageAsync(
        Conversation conversation,
        AiChatResult result,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = result.Content,
            Model = result.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            StopReason = result.StopReason,
            LatencyMs = (int)Math.Min(int.MaxValue, elapsed.TotalMilliseconds)
        };

        _conversations.AddMessage(message);
        conversation.Touch();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return message;
    }
}
