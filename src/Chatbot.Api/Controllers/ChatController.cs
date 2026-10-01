using Chatbot.Api.Infrastructure;
using Chatbot.Application.DTOs;
using Chatbot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Chatbot.Api.Controllers;

/// <summary>
/// Request/response chat endpoint. The web UI uses the SignalR hub (<c>/hubs/chat</c>) for streaming;
/// this endpoint serves clients that just want the complete answer.
/// </summary>
[ApiController]
[Authorize]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chat;

    public ChatController(IChatService chat) => _chat = chat;

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Chat)]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<ChatResponse> Send(ChatRequest request, CancellationToken cancellationToken) =>
        _chat.SendMessageAsync(User.GetUserId(), request, observer: null, cancellationToken);
}
