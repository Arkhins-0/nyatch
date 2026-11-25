using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using Nyatch.Core;

namespace Nyatch.App;

// The chat pages' attachments and mic: the + button (a screenshot, a picture, a file) and speaking a question.
// Pictures go to the LLMs that can see them, PDFs to those that read them; a text file's words go into the question.
internal sealed partial class DeckWindow
{
    private const long MaxPicture = 8 * 1024 * 1024, MaxPdf = 16 * 1024 * 1024, MaxText = 200 * 1024;
    private static readonly string[] PictureTypes = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>The + button's menu: a screenshot, a picture or a file, as the LLM on show can take them.</summary>
    private void AttachMenu(AiPage page)
    {
        var sees = Assistant.SeesPictures(page.Engine);
        var choices = new List<(string, Action)>();
        if (sees)
        {
            choices.Add(("Screenshot of the screen", () => _ = AttachScreenshot(page)));
            choices.Add(("Picture…", () => AttachFile(page, pictures: true)));
        }
        choices.Add(("File…", () => AttachFile(page, pictures: false)));
        ItemMenu([.. choices]);
    }

    private void AttachFile(AiPage page, bool pictures)
    {
        var filter = pictures
            ? "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp"
            : "Files you can send|*.*";
        var pick = new Microsoft.Win32.OpenFileDialog { Title = pictures ? "Send a picture" : "Send a file", Filter = filter, Multiselect = true };
        if (pick.ShowDialog() != true) return;
        foreach (var path in pick.FileNames)
        {
            if (Attachment(page, path) is { } file) page.Files.Add(file);
        }
        AiChanged(page);
        AiFocusField();
    }

    /// <summary>A file as an attachment the LLM on show can take, or null (and a note why).</summary>
    private ChatFile? Attachment(AiPage page, string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            var size = new FileInfo(path).Length;
            if (PictureTypes.Contains(ext))
            {
                if (!Assistant.SeesPictures(page.Engine)) return Refuse(page, $"{Assistant.Name(page.Engine)} cannot see pictures.");
                if (size > MaxPicture) return Refuse(page, $"{name} is over 8 MB.");
                var type = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/png" };
                return new ChatFile(name, type, Convert.ToBase64String(File.ReadAllBytes(path)));
            }
            if (ext == ".pdf")
            {
                if (!Assistant.ReadsPdfs(page.Engine)) return Refuse(page, $"{Assistant.Name(page.Engine)} cannot read PDFs: Claude, ChatGPT and Gemini can.");
                if (size > MaxPdf) return Refuse(page, $"{name} is over 16 MB.");
                return new ChatFile(name, "application/pdf", Convert.ToBase64String(File.ReadAllBytes(path)));
            }
            // Anything else is sent as its words, if it is text.
            if (size > MaxText) return Refuse(page, $"{name} is too long to send as text (over 200 KB).");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Contains((byte)0)) return Refuse(page, $"{name} is not a text file, a picture or a PDF.");
            return new ChatFile(name, "text/plain", Encoding.UTF8.GetString(bytes));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Refuse(page, $"{name} could not be read.");
        }
    }

    private ChatFile? Refuse(AiPage page, string why)
    {
        page.Answer = why;
        page.AnswerIsNote = true;
        page.Asking ??= "";
        AiChanged(page);
        return null;
    }

    // ---------- A screenshot ----------

    /// <summary>The screen the pointer is on, as it is without the panel over it.</summary>
    private async Task AttachScreenshot(AiPage page)
    {
        // The panel steps aside for the picture, then comes back.
        var side = SideOn && _sideOpen;
        if (side) Island.SideOpen(false);
        await Task.Delay(side ? 450 : 150);
        try
        {
            GetCursorPos(out var at);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(at.X, at.Y)).Bounds;
            using var bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, bitmap.Size);
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            page.Files.Add(new ChatFile($"Screenshot {DateTime.Now:HH.mm.ss}.png", "image/png", Convert.ToBase64String(stream.ToArray())));
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        {
            Refuse(page, "The screenshot could not be taken.");
        }
        if (side) OpenSide();
        AiChanged(page);
    }

    /// <summary>The files of a question as they are sent: pictures and PDFs as files, text files' words added to the text.</summary>
    private static (string Text, List<ChatFile> Files) WithFiles(string question, List<ChatFile> files)
    {
        var text = new StringBuilder(question);
        foreach (var file in files.Where(f => f.MediaType == "text/plain"))
        {
            text.Append(text.Length > 0 ? "\n\n" : "").Append("File ").Append(file.Name).Append(":\n```\n").Append(file.Data).Append("\n```");
        }
        return (text.ToString(), files.Where(f => f.MediaType != "text/plain").ToList());
    }

    // ---------- The mic ----------

    private System.Speech.Recognition.SpeechRecognitionEngine? _listening;
    private AiPage? _listeningFor;

    /// <summary>Start or stop listening: what is said is typed into the question, at its end.</summary>
    private void Mic(AiPage page)
    {
        if (_listening is { } running)
        {
            StopListening();
            return;
        }
        try
        {
            var engine = new System.Speech.Recognition.SpeechRecognitionEngine();
            engine.LoadGrammar(new System.Speech.Recognition.DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, heard) => Dispatcher.BeginInvoke(() =>
            {
                if (_listeningFor is not { } to || heard.Result?.Text is not { Length: > 0 } words) return;
                to.Draft = to.Draft.Length == 0 ? Capital(words) : to.Draft.TrimEnd() + " " + words;
                AiSetEditText(to.Draft);
                AiChanged(to);
            });
            engine.RecognizeAsync(System.Speech.Recognition.RecognizeMode.Multiple);
            _listening = engine;
            _listeningFor = page;
            page.Listening = true;
        }
        catch (Exception error) when (error is InvalidOperationException or PlatformNotSupportedException or COMException or ArgumentException)
        {
            Refuse(page, "No microphone, or Windows speech recognition is not set up (Settings, Time & language, Speech).");
        }
        AiChanged(page);
    }

    private void StopListening()
    {
        if (_listening is not { } running) return;
        _listening = null;
        try
        {
            running.RecognizeAsyncCancel();
        }
        catch (InvalidOperationException)
        {
        }
        running.Dispose();
        if (_listeningFor is { } page)
        {
            page.Listening = false;
            _listeningFor = null;
            AiChanged(page);
        }
    }

    private static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}
