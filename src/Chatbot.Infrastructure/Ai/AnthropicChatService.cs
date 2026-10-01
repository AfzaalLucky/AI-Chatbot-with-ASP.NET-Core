using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiChatService"/> backed by the Claude Messages API via the official Anthropic SDK.
/// Always streams, so long answers never hit HTTP timeouts and the UI can render text progressively.
/// Uses the beta endpoint only to opt into server-side refusal fallbacks.
/// </summary>
internal sealed class AnthropicChatService : IAiChatService
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";
    internal const string RefusalNotice =
        "I'm sorry, but I can't help with that request.";

    private readonly AnthropicClient _client;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicChatService> _logger;

    public AnthropicChatService(AnthropicClient client, IOptions<AnthropicOptions> options, ILogger<AnthropicChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiChatResult> GenerateResponseAsync(
        IReadOnlyList<AiChatMessage> history,
        Func<string, CancellationToken, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        if (history.Count == 0)
        {
            throw new ArgumentException("History must contain at least one message.", nameof(history));
        }

        var parameters = BuildParameters(history);
        var content = new StringBuilder();
        string model = _options.Model;
        int? inputTokens = null;
        int? outputTokens = null;
        string? stopReason = null;

        try
        {
            await foreach (var streamEvent in _client.Beta.Messages.CreateStreaming(parameters, cancellationToken))
            {
                if (streamEvent.TryPickContentBlockDelta(out var blockDelta) &&
                    blockDelta.Delta.TryPickText(out var text))
                {
                    content.Append(text.Text);
                    if (onDelta is not null)
                    {
                        await onDelta(text.Text, cancellationToken);
                    }
                }
                else if (streamEvent.TryPickStart(out var start))
                {
                    // With fallbacks enabled, message_start names the model that actually served the turn.
                    model = start.Message.Model.Raw() ?? model;
                    inputTokens = ToInt(start.Message.Usage.InputTokens);
                }
                else if (streamEvent.TryPickDelta(out var messageDelta))
                {
                    stopReason = messageDelta.Delta.StopReason?.Raw();
                    outputTokens = ToInt(messageDelta.Usage.OutputTokens);
                    inputTokens = ToInt(messageDelta.Usage.InputTokens) ?? inputTokens;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AnthropicRateLimitException ex)
        {
            _logger.LogWarning("Anthropic rate limit hit: {Status}", ex.GetType().Name);
            throw new AiServiceException("The AI service is busy right now. Please try again in a moment.", isTransient: true, ex);
        }
        catch (Anthropic5xxException ex)
        {
            _logger.LogWarning("Anthropic server error: {Status}", ex.GetType().Name);
            throw new AiServiceException("The AI service is temporarily unavailable. Please try again.", isTransient: true, ex);
        }
        catch (AnthropicIOException ex)
        {
            _logger.LogWarning("Network error calling Anthropic: {Error}", ex.GetType().Name);
            throw new AiServiceException("Could not reach the AI service. Please try again.", isTransient: true, ex);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            _logger.LogError("Anthropic rejected the configured credentials");
            throw new AiServiceException("The AI service is not configured correctly.", isTransient: false, ex);
        }
        catch (AnthropicApiException ex)
        {
            // Message may echo request content, so only the exception type is logged.
            _logger.LogError("Anthropic API error: {Error}", ex.GetType().Name);
            throw new AiServiceException("The AI service could not process this request.", isTransient: false, ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TimeoutException or ArgumentException)
        {
            // Includes missing credentials and client-side timeouts.
            _logger.LogError(ex, "Unexpected failure calling Anthropic");
            throw new AiServiceException("The AI service is currently unavailable.", isTransient: true, ex);
        }

        if (string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase))
        {
            // Partial output from a declined turn must not be treated as a complete answer.
            _logger.LogInformation("Model declined the request (stop_reason=refusal)");
            var notice = content.Length == 0 ? RefusalNotice : "\n\n_" + RefusalNotice + "_";
            content.Append(notice);
            if (onDelta is not null)
            {
                await onDelta(notice, cancellationToken);
            }
        }
        else if (string.Equals(stopReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Response truncated at max_tokens ({MaxTokens})", _options.MaxTokens);
        }

        if (content.Length == 0)
        {
            throw new AiServiceException("The AI service returned an empty response.", isTransient: true);
        }

        return new AiChatResult(content.ToString(), model, inputTokens, outputTokens, stopReason);
    }

    private MessageCreateParams BuildParameters(IReadOnlyList<AiChatMessage> history)
    {
        var messages = history
            .Select(m => new BetaMessageParam
            {
                Role = m.Role == MessageRole.User ? Role.User : Role.Assistant,
                Content = m.Content
            })
            .ToList();

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = _options.SystemPrompt,
            Messages = messages
        };

        if (ParseEffort(_options.Effort) is { } effort)
        {
            parameters = parameters with { OutputConfig = new BetaOutputConfig { Effort = effort } };
        }

        if (_options.EnableRefusalFallback)
        {
            parameters = parameters with { Betas = [FallbackBeta], Fallbacks = new Default() };
        }

        return parameters;
    }

    private static Effort? ParseEffort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => null
    };

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(int.MaxValue, value.Value);
}
