using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

// What the second notch's pages take from the settings: the pomodoro's lengths, how much the
// clipboard keeps, and the user's own instructions for the chats.
public class DeckFeatureSettingsTests
{
    [Fact]
    public void TheClipboardKeepsAsManyAsItIsTold()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clips = new ClipHistory(folder);
            var now = new DateTime(2026, 11, 22, 9, 0, 0);
            for (var i = 0; i < 12; i++) clips.AddText("copy " + i, now.AddSeconds(i));
            Assert.Equal(12, clips.Items.Count);
            // Lowered: the oldest go at once; the newest stay.
            clips.Keep = 5;
            Assert.Equal(5, clips.Items.Count);
            Assert.Contains(clips.Items, i => i.Text == "copy 11");
            Assert.DoesNotContain(clips.Items, i => i.Text == "copy 0");
            clips.AddText("one more", now.AddSeconds(60));
            Assert.Equal(5, clips.Items.Count);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheUsersInstructionsGoWithEveryQuestion()
    {
        Assert.Equal(Assistant.Instructions, Assistant.InstructionsWith("  "));
        // A prompt from the settings takes the place of Nyatch's; empty, Nyatch's is used.
        Assert.Equal("Answer in detail.", Assistant.InstructionsWith("", " Answer in detail. "));
        Assert.Equal(Assistant.Instructions, Assistant.InstructionsWith("", "  "));
        Assert.StartsWith("Answer in detail. The user asks", Assistant.InstructionsWith("Be kind.", "Answer in detail."));
        var own = Assistant.InstructionsWith(" Answer in French. ");
        Assert.StartsWith(Assistant.Instructions, own);
        Assert.EndsWith("Answer in French.", own);

        var turns = new List<ChatTurn> { new("user", "Hello") };
        Assert.Contains("Answer in French.", Assistant.OpenAiBody("m", turns, "Answer in French."));
        Assert.Contains("Answer in French.", Assistant.GeminiBody(turns, search: false, "Answer in French."));
        Assert.DoesNotContain("The user asks this", Assistant.OpenAiBody("m", turns));
    }
}
