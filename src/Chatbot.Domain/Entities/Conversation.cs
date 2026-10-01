namespace Chatbot.Domain.Entities;

public class Conversation
{
    public const int MaxTitleLength = 200;
    public const string DefaultTitle = "New conversation";

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = DefaultTitle;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();

    /// <summary>Derives a title from the first user message when the conversation still has the default title.</summary>
    public void EnsureTitleFrom(string firstMessage)
    {
        if (Title != DefaultTitle || string.IsNullOrWhiteSpace(firstMessage))
        {
            return;
        }

        var singleLine = string.Join(' ', firstMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Title = singleLine.Length <= 60 ? singleLine : singleLine[..57].TrimEnd() + "...";
    }

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
