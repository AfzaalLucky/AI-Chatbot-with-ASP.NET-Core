using Chatbot.Api.Infrastructure;
using Chatbot.Application.DTOs;
using Chatbot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversations;

    public ConversationsController(IConversationService conversations) => _conversations = conversations;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ConversationSummaryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ConversationSummaryDto>> List(CancellationToken cancellationToken) =>
        _conversations.ListAsync(User.GetUserId(), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConversationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ConversationDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        _conversations.GetAsync(User.GetUserId(), id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<ConversationSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConversationSummaryDto>> Create(
        [FromBody] CreateConversationRequest? request,
        CancellationToken cancellationToken)
    {
        var created = await _conversations.CreateAsync(User.GetUserId(), request ?? new CreateConversationRequest(null), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _conversations.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
