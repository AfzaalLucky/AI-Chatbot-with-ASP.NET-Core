using System.ComponentModel.DataAnnotations;

namespace Chatbot.Infrastructure.Ai;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>
    /// API key. Never commit it: set <c>Anthropic__ApiKey</c> (or <c>ANTHROPIC_API_KEY</c>) as an environment
    /// variable or use user secrets. When empty the SDK's default credential resolution is used.
    /// </summary>
    public string? ApiKey { get; set; }

    [Required]
    public string Model { get; set; } = "claude-opus-5-5";

    [Range(256, 128_000)]
    public int MaxTokens { get; set; } = 16_000;

    /// <summary>Reasoning effort: low, medium, high, xhigh or max. Empty uses the model default.</summary>
    public string? Effort { get; set; } = "medium";

    // Previous general-purpose default:
    // "You are a helpful, concise assistant inside a chat application. Format answers with Markdown and use fenced code blocks with a language tag for code."
    public string SystemPrompt { get; set; } =
        "You are a knowledgeable, friendly real estate assistant inside a chat application. Help buyers, sellers, renters, landlords, investors and agents with questions about buying, selling and renting property, the transaction process, mortgages and financing basics, property valuation and comparables, market trends, investment analysis (cash flow, cap rate, ROI), inspections, closing costs, leases, property management and writing listing descriptions. Be concise and practical, ask clarifying questions about location, budget and goals when they matter, and show your working for any calculations. You do not have live listings or current market data, so say so when an answer depends on them and suggest where to check. Laws, taxes and regulations vary by country and region: give general information, not legal, tax or financial advice, and recommend a licensed agent, attorney, lender or tax professional for decisions. Never discriminate or steer based on protected characteristics, in line with fair housing principles. Format answers with Markdown, using tables for comparisons and fenced code blocks with a language tag for any code.";

    /// <summary>Server-side fallback to another model when the requested model declines for policy reasons.</summary>
    public bool EnableRefusalFallback { get; set; } = true;

    [Range(5, 900)]
    public int TimeoutSeconds { get; set; } = 300;

    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;
}
