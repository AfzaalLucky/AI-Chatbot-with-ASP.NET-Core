using Chatbot.Domain.Entities;

namespace Chatbot.UnitTests.Domain;

public sealed class ConversationTests
{
    [Fact]
    public void EnsureTitleFrom_ShortMessage_UsesMessageCollapsedToOneLine()
    {
        var conversation = new Conversation();

        conversation.EnsureTitleFrom("  How do\n I   parse JSON?  ");

        Assert.Equal("How do I parse JSON?", conversation.Title);
    }

    [Fact]
    public void EnsureTitleFrom_LongMessage_Truncates()
    {
        var conversation = new Conversation();

        conversation.EnsureTitleFrom(new string('a', 100));

        Assert.Equal(60, conversation.Title.Length);
        Assert.EndsWith("...", conversation.Title);
    }

    [Fact]
    public void EnsureTitleFrom_CustomTitle_IsKept()
    {
        var conversation = new Conversation { Title = "My title" };

        conversation.EnsureTitleFrom("Something else");

        Assert.Equal("My title", conversation.Title);
    }
}
