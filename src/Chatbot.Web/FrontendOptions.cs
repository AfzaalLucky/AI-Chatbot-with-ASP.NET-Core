using System.ComponentModel.DataAnnotations;

namespace Chatbot.Web;

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Public base URL of Chatbot.Api, e.g. https://api.example.com. The browser calls it directly.</summary>
    [Required, Url]
    public string ApiBaseUrl { get; set; } = string.Empty;
}
