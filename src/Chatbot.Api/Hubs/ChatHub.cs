using Chatbot.Api.Infrastructure;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.DTOs;
using Chatbot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chatbot.Api.Hubs;

/// <summary>Server-to-client callbacks. Method names are what the JavaScript client subscribes to.</summary>
public interface IChatClient
{
    Task UserMessageSaved(Guid conversationId, string conversationTitle, MessageDto message);
    Task ReceiveDelta(Guid conversationId, string text);
}

/// <summary>
/// Real-time chat. The client invokes <see cref="SendMessage"/>; generated text is pushed back through
/// <see cref="IChatClient.ReceiveDelta"/> as it arrives and the invocation completes with the final
/// <see cref="ChatResponse"/>. If the connection drops, generation is cancelled and any partial reply is saved.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    private readonly IChatService _chat;
    private readonly ChatRateLimiter _rateLimiter;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IChatService chat, ChatRateLimiter rateLimiter, ILogger<ChatHub> logger)
    {
        _chat = chat;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<ChatResponse> SendMessage(ChatRequest request)
    {
        var userId = Context.User.GetUserId();

        using var lease = _rateLimiter.Acquire("user:" + userId);
        if (!lease.IsAcquired)
        {
            throw new HubException("Rate limit exceeded. Please wait a moment before sending another message.");
        }

        try
        {
            return await _chat.SendMessageAsync(userId, request, new CallerObserver(Clients.Caller), Context.ConnectionAborted);
        }
        catch (ValidationException ex)
        {
            throw new HubException(string.Join(" ", ex.Errors.SelectMany(e => e.Value)));
        }
        catch (AppException ex)
        {
            // Application exception messages are written to be client-safe.
            throw new HubException(ex.Message);
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Connection {ConnectionId} closed during generation", Context.ConnectionId);
            throw new HubException("The connection was closed before the response completed.");
        }
    }

    private sealed class CallerObserver : IChatStreamObserver
    {
        private readonly IChatClient _caller;

        public CallerObserver(IChatClient caller) => _caller = caller;

        public Task OnUserMessageSavedAsync(Guid conversationId, string conversationTitle, MessageDto userMessage, CancellationToken cancellationToken) =>
            _caller.UserMessageSaved(conversationId, conversationTitle, userMessage);

        public Task OnDeltaAsync(Guid conversationId, string text, CancellationToken cancellationToken) =>
            _caller.ReceiveDelta(conversationId, text);
    }
}
