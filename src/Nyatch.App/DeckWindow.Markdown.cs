using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nyatch.App;

// Markdown, as the assistants write it, for the chat pages the island draws: headings, bold and italic,
// lists, tables, code, quotes, links and pictures. Each block goes to the island as plain text with
// ranges for its formatting (see the island's Rich), so the text stays selectable there.
internal sealed partial class DeckWindow
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$")] private static partial Regex RuleLine();
    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$")] private static partial Regex ListLine();
    [GeneratedRegex(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$")] private static partial Regex TableRuleLine();
    [GeneratedRegex(@"^\s*(```|~~~)")] private static partial Regex FenceLine();

    // Formatting flags, as the island reads them.
    private const int Bold = 1, Italic = 2, Code = 4, Struck = 8, Link = 16, Quiet = 32;

    private sealed record MdSpan(int Start, int Length, int Flags, int Action);

    /// <summary>
    /// One block: P paragraph, H heading (Level 1-6), L list item (Level its indent, Marker its bullet or
    /// number), C code, Q quote, R rule, T table (Level its columns, then Cells), I picture.
    /// </summary>
    private sealed class MdBlock
    {
        public char Kind { get; init; } = 'P';
        public int Level { get; init; }
        public string Marker { get; init; } = "";
        public string Text { get; set; } = "";
        public List<MdSpan> Spans { get; } = [];
        public string? Picture { get; init; }
        public int Action { get; init; } = -1;
        public List<(int Row, int Col, string Text, List<MdSpan> Spans)> Cells { get; } = [];
    }

    /// <summary>What the island needs to turn a link into a click, and a picture into a file.</summary>
    private sealed class MdContext(Func<Uri, int> link)
    {
        public Func<Uri, int> Link { get; } = link;
        public List<(Uri Address, string Label)> Pictures { get; } = [];
    }

    /// <summary>A reply's Markdown as blocks.</summary>
    private static List<MdBlock> MarkdownBlocks(string markdown, Func<Uri, int> link, Func<Uri, string?>? picture = null)
    {
        var blocks = new List<MdBlock>();
        var context = new MdContext(link);
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;

        // A block of formatted text, then any pictures it had in it.
        void Text(char kind, string text, int level = 0, string marker = "", bool several = true)
        {
            var block = new MdBlock { Kind = kind, Level = level, Marker = marker };
            var output = new StringBuilder();
            if (several) InlineLines(text, output, block.Spans, 0, -1, context);
            else Inline(text, output, block.Spans, 0, -1, context);
            block.Text = output.ToString();
            if (block.Text.Length > 0 || context.Pictures.Count == 0) blocks.Add(block);
            PicturesOut();
        }
        void PicturesOut()
        {
            foreach (var (address, label) in context.Pictures)
            {
                blocks.Add(new MdBlock { Kind = 'I', Text = label, Picture = picture?.Invoke(address), Action = context.Link(address) });
            }
            context.Pictures.Clear();
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // ``` code ```
            if (FenceLine().IsMatch(line))
            {
                var fence = line.TrimStart()[..3];
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                {
                    code.Append(lines[i]).Append('\n');
                    i++;
                }
                i++;  // the closing fence (or the end, while the reply is still coming)
                blocks.Add(new MdBlock { Kind = 'C', Text = code.ToString().TrimEnd('\n') });
                continue;
            }

            if (HeadingLine().Match(line) is { Success: true } heading)
            {
                Text('H', heading.Groups[2].Value, heading.Groups[1].Length, several: false);
                i++;
                continue;
            }

            if (RuleLine().IsMatch(line))
            {
                blocks.Add(new MdBlock { Kind = 'R' });
                i++;
                continue;
            }

            // | a | b | with a |---|---| line under the first row
            if (line.Contains('|') && i + 1 < lines.Length && lines[i + 1].Contains('-') && TableRuleLine().IsMatch(lines[i + 1]))
            {
                var rows = new List<string[]> { Cells(line) };
                i += 2;
                while (i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    rows.Add(Cells(lines[i]));
                    i++;
                }
                var table = new MdBlock { Kind = 'T', Level = rows.Max(r => r.Length) };
                for (var r = 0; r < rows.Count; r++)
                {
                    for (var c = 0; c < rows[r].Length; c++)
                    {
                        var output = new StringBuilder();
                        var spans = new List<MdSpan>();
                        Inline(rows[r][c], output, spans, 0, -1, context);
                        table.Cells.Add((r, c, output.ToString(), spans));
                    }
                }
                context.Pictures.Clear();
                blocks.Add(table);
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var text = lines[i].TrimStart()[1..];
                    quote.Append(text.StartsWith(' ') ? text[1..] : text).Append('\n');
                    i++;
                }
                Text('Q', quote.ToString().TrimEnd('\n'));
                continue;
            }

            if (ListLine().Match(line) is { Success: true } item)
            {
                // Each item has its bullet or number hanging in the margin.
                var text = new StringBuilder(item.Groups[3].Value);
                var indent = item.Groups[1].Value.Replace("\t", "    ").Length;
                var marker = item.Groups[2].Value;
                i++;
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !ListLine().IsMatch(lines[i]) && lines[i].StartsWith(' ') && !FenceLine().IsMatch(lines[i]))
                {
                    text.Append('\n').Append(lines[i].Trim());
                    i++;
                }
                Text('L', text.ToString(), 16 + Math.Min(4, indent / 2) * 14, marker.Length == 1 ? "•" : marker);
                continue;
            }

            // A paragraph: lines up to a blank one or another kind of block.
            var body = new StringBuilder(line);
            i++;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !FenceLine().IsMatch(lines[i]) && !HeadingLine().IsMatch(lines[i]) &&
                   !ListLine().IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith('>') && !RuleLine().IsMatch(lines[i]) &&
                   !(lines[i].Contains('|') && i + 1 < lines.Length && TableRuleLine().IsMatch(lines[i + 1]) && lines[i + 1].Contains('-')))
            {
                body.Append('\n').Append(lines[i]);
                i++;
            }
            Text('P', body.ToString());
        }
        return blocks;
    }

    /// <summary>
    /// An answer's Markdown as text in a window (the settings window's chat history): headings, lists,
    /// code, quotes, tables and links as they should look, selectable and copyable, rather than its marks.
    /// </summary>
    internal static System.Windows.FrameworkElement MarkdownView(string markdown, System.Windows.Media.Brush ink, System.Windows.Media.Brush soft,
        System.Windows.Media.Brush accent, System.Windows.Media.Brush codeBack)
    {
        var links = new List<Uri>();
        var blocks = MarkdownBlocks(markdown, address =>
        {
            links.Add(address);
            return links.Count - 1;
        });
        var mono = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
        var document = new System.Windows.Documents.FlowDocument
        {
            PagePadding = new System.Windows.Thickness(0),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13.5,
            Foreground = ink,
            LineHeight = 20,
        };
        System.Windows.Documents.Inline Run(string text, int flags, int action)
        {
            var run = new System.Windows.Documents.Run(text);
            if ((flags & Bold) != 0) run.FontWeight = System.Windows.FontWeights.SemiBold;
            if ((flags & Italic) != 0) run.FontStyle = System.Windows.FontStyles.Italic;
            if ((flags & Struck) != 0) run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
            if ((flags & Quiet) != 0) run.Foreground = soft;
            if ((flags & Code) != 0)
            {
                run.FontFamily = mono;
                run.FontSize = 12.5;
                run.Background = codeBack;
            }
            if (action >= 0 && action < links.Count)
            {
                var address = links[action];
                var link = new System.Windows.Documents.Hyperlink(run) { Foreground = accent, ToolTip = address.ToString(), Cursor = System.Windows.Input.Cursors.Hand };
                link.Click += (_, _) => OpenAddress(address);
                return link;
            }
            return run;
        }
        void Fill(System.Windows.Documents.Paragraph paragraph, string text, List<MdSpan> spans)
        {
            var at = 0;
            foreach (var span in spans.OrderBy(s => s.Start))
            {
                if (span.Start < at) continue;
                if (span.Start > at) paragraph.Inlines.Add(Run(text[at..span.Start], 0, -1));
                var end = Math.Min(text.Length, span.Start + span.Length);
                paragraph.Inlines.Add(Run(text[span.Start..end], span.Flags, span.Action));
                at = end;
            }
            if (at < text.Length) paragraph.Inlines.Add(Run(text[at..], 0, -1));
        }
        foreach (var block in blocks)
        {
            var paragraph = new System.Windows.Documents.Paragraph { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            switch (block.Kind)
            {
                case 'H':
                    paragraph.FontSize = block.Level <= 1 ? 19 : block.Level == 2 ? 16.5 : 14.5;
                    paragraph.FontWeight = System.Windows.FontWeights.SemiBold;
                    paragraph.Margin = new System.Windows.Thickness(0, 6, 0, 6);
                    Fill(paragraph, block.Text, block.Spans);
                    break;
                case 'L':
                    // The bullet or number hangs in the margin.
                    paragraph.Margin = new System.Windows.Thickness(block.Level, 0, 0, 3);
                    paragraph.TextIndent = -14;
                    paragraph.Inlines.Add(new System.Windows.Documents.Run((block.Marker == "•" ? "•" : block.Marker) + "  ") { Foreground = soft });
                    Fill(paragraph, block.Text, block.Spans);
                    break;
                case 'C':
                    paragraph.FontFamily = mono;
                    paragraph.FontSize = 12.5;
                    paragraph.Background = codeBack;
                    paragraph.Padding = new System.Windows.Thickness(10, 8, 10, 8);
                    paragraph.Inlines.Add(new System.Windows.Documents.Run(block.Text));
                    break;
                case 'Q':
                    paragraph.BorderBrush = accent;
                    paragraph.BorderThickness = new System.Windows.Thickness(3, 0, 0, 0);
                    paragraph.Padding = new System.Windows.Thickness(10, 2, 0, 2);
                    paragraph.Foreground = soft;
                    Fill(paragraph, block.Text, block.Spans);
                    break;
                case 'R':
                    paragraph.BorderBrush = soft;
                    paragraph.BorderThickness = new System.Windows.Thickness(0, 0, 0, 0.6);
                    paragraph.Margin = new System.Windows.Thickness(0, 2, 0, 10);
                    paragraph.FontSize = 2;
                    break;
                case 'T':
                {
                    var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new System.Windows.Thickness(0, 0, 0, 10) };
                    for (var c = 0; c < block.Level; c++) table.Columns.Add(new System.Windows.Documents.TableColumn());
                    var group = new System.Windows.Documents.TableRowGroup();
                    foreach (var rowCells in block.Cells.GroupBy(cell => cell.Row).OrderBy(g => g.Key))
                    {
                        var row = new System.Windows.Documents.TableRow();
                        foreach (var cell in rowCells.OrderBy(cell => cell.Col))
                        {
                            var inside = new System.Windows.Documents.Paragraph { Margin = new System.Windows.Thickness(0) };
                            if (cell.Row == 0) inside.FontWeight = System.Windows.FontWeights.SemiBold;
                            Fill(inside, cell.Text, cell.Spans);
                            row.Cells.Add(new System.Windows.Documents.TableCell(inside)
                            {
                                Padding = new System.Windows.Thickness(8, 4, 8, 4),
                                BorderBrush = soft,
                                BorderThickness = new System.Windows.Thickness(0, 0, 0, 0.5),
                            });
                        }
                        group.Rows.Add(row);
                    }
                    table.RowGroups.Add(group);
                    document.Blocks.Add(table);
                    continue;
                }
                case 'I':
                    paragraph.Inlines.Add(Run(block.Text.Length > 0 ? block.Text : "Picture", Link, block.Action));
                    break;
                default:
                    Fill(paragraph, block.Text, block.Spans);
                    break;
            }
            document.Blocks.Add(paragraph);
        }
        if (document.Blocks.LastBlock is { } last) last.Margin = new System.Windows.Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return new System.Windows.Controls.RichTextBox
        {
            Document = document,
            IsReadOnly = true,
            IsDocumentEnabled = true,  // the links can be clicked
            BorderThickness = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new System.Windows.Thickness(0),
            Foreground = ink,
        };
    }

    private static string[] Cells(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal)) text = text[..^1];
        return Regex.Split(text, @"(?<!\\)\|").Select(c => c.Trim().Replace("\\|", "|")).ToArray();
    }

    /// <summary>Text of several lines: each line break is kept.</summary>
    private static void InlineLines(string text, StringBuilder output, List<MdSpan> spans, int flags, int action, MdContext context)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) output.Append('\n');
            Inline(lines[i].TrimEnd(), output, spans, flags, action, context);
        }
    }

    /// <summary>One line's bold, italic, code, links and pictures, as text and ranges.</summary>
    private static void Inline(string text, StringBuilder output, List<MdSpan> spans, int flags, int action, MdContext context)
    {
        var plain = new StringBuilder();
        void Emit(string words, int f, int a)
        {
            if (words.Length == 0) return;
            if (f != 0 || a >= 0) spans.Add(new MdSpan(output.Length, words.Length, f, a));
            output.Append(words);
        }
        void Flush()
        {
            Emit(plain.ToString(), flags, action);
            plain.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && "\\`*_{}[]()#+-.!|~>".Contains(text[i + 1]))
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                var ticks = 1;
                while (i + ticks < text.Length && text[i + ticks] == '`') ticks++;
                var close = text.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                if (close > 0)
                {
                    Flush();
                    Emit(text[(i + ticks)..close].Trim(), flags | Code, action);
                    i = close + ticks;
                    continue;
                }
            }
            if ((c == '!' && i + 1 < text.Length && text[i + 1] == '[') || c == '[')
            {
                var picture = c == '!';
                var open = picture ? i + 1 : i;
                var close = ClosingBracket(text, open, '[', ']');
                if (close > 0 && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var end = ClosingBracket(text, close + 1, '(', ')');
                    if (end > 0)
                    {
                        var label = text[(open + 1)..close];
                        var target = text[(close + 2)..end].Trim();
                        var space = target.IndexOf(' ');
                        if (space > 0) target = target[..space];  // drop a "title"
                        target = target.Trim('<', '>');
                        if (Uri.TryCreate(target, UriKind.Absolute, out var address) && address.Scheme is "http" or "https")
                        {
                            Flush();
                            if (picture) context.Pictures.Add((address, label));
                            else Inline(label, output, spans, flags | Link, context.Link(address), context);
                            i = end + 1;
                            continue;
                        }
                    }
                }
            }
            if (c is '*' or '_' or '~')
            {
                var count = 1;
                while (i + count < text.Length && text[i + count] == c && count < 3) count++;
                if (c == '~' && count < 2)
                {
                    plain.Append(c);
                    i++;
                    continue;
                }
                if (c == '~') count = 2;
                // An underscore inside a word (snake_case) is just an underscore.
                var wordBefore = i > 0 && char.IsLetterOrDigit(text[i - 1]);
                var opens = i + count < text.Length && !char.IsWhiteSpace(text[i + count]) && !(c == '_' && wordBefore);
                var marker = new string(c, count);
                var close = opens ? ClosingMarker(text, i + count, marker) : -1;
                if (close > 0)
                {
                    Flush();
                    var more = c == '~' ? Struck : count == 1 ? Italic : count == 2 ? Bold : Bold | Italic;
                    Inline(text[(i + count)..close], output, spans, flags | more, action, context);
                    i = close + count;
                    continue;
                }
                plain.Append(marker);
                i += count;
                continue;
            }
            // A bare address.
            if (c == 'h' && (text.AsSpan(i).StartsWith("https://") || text.AsSpan(i).StartsWith("http://")))
            {
                var end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('<' or '>' or '"')) end++;
                while (end > i && ".,;:!?)]'".Contains(text[end - 1])) end--;
                if (Uri.TryCreate(text[i..end], UriKind.Absolute, out var address))
                {
                    Flush();
                    Emit(text[i..end], flags | Link, context.Link(address));
                    i = end;
                    continue;
                }
            }
            plain.Append(c);
            i++;
        }
        Flush();
    }

    /// <summary>The bracket that closes the one at <paramref name="open"/>, or -1.</summary>
    private static int ClosingBracket(string text, int open, char left, char right)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] == left) depth++;
            else if (text[i] == right && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Where the emphasis opened before <paramref name="from"/> closes: the same marker, not after a space.</summary>
    private static int ClosingMarker(string text, int from, string marker)
    {
        for (var i = from; i + marker.Length <= text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] == '`')
            {
                var next = text.IndexOf('`', i + 1);
                if (next > 0) i = next;
                continue;
            }
            if (string.CompareOrdinal(text, i, marker, 0, marker.Length) != 0 || char.IsWhiteSpace(text[i - 1])) continue;
            // Not the start of a longer run of the same character, and not mid-word for underscores.
            var after = i + marker.Length;
            if (after < text.Length && text[after] == marker[0])
            {
                while (after < text.Length && text[after] == marker[0]) after++;
                i = after - 1;
                continue;
            }
            if (marker[0] == '_' && after < text.Length && char.IsLetterOrDigit(text[after])) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Blocks as the island's records (B, and c for a table's cells).</summary>
    private static void BlockRecords(List<MdBlock> blocks, List<string> records)
    {
        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
        static string Spans(List<MdSpan> spans) => string.Join(';', spans.Select(s => $"{s.Start},{s.Length},{s.Flags},{s.Action}"));
        foreach (var block in blocks)
        {
            records.Add(string.Join('\u001F', "B", block.Kind.ToString(), block.Level.ToString(), Clean(block.Marker), Clean(block.Text), Spans(block.Spans),
                Clean(block.Picture ?? ""), block.Action.ToString()));
            foreach (var (row, col, text, spans) in block.Cells)
            {
                records.Add(string.Join('\u001F', "c", row.ToString(), col.ToString(), Clean(text), Spans(spans)));
            }
        }
    }

    // ----- Files from the web (pictures in replies, sites' icons), kept in a folder so the island can read them -----
    private readonly Dictionary<string, string?> _webFiles = [];
    private static readonly string WebFolder = Path.Combine(Path.GetTempPath(), "Nyatch", "web");

    /// <summary>The file for a web address once it has been fetched, else null (and it is fetched; the page is sent again then).</summary>
    private string? WebFile(Uri address)
    {
        var key = address.AbsoluteUri;
        if (_webFiles.TryGetValue(key, out var known)) return known;
        var file = Path.Combine(WebFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".img");
        if (File.Exists(file))
        {
            _webFiles[key] = file;
            return file;
        }
        _webFiles[key] = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await Web.GetByteArrayAsync(address);
                if (bytes.Length == 0 || bytes.Length > 15_000_000) return;
                Directory.CreateDirectory(WebFolder);
                await File.WriteAllBytesAsync(file, bytes);
                await Dispatcher.InvokeAsync(() =>
                {
                    _webFiles[key] = file;
                    SendNativeData();
                });
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
            {
            }
        });
        return null;
    }

    private static void OpenAddress(Uri address)
    {
        if (address.Scheme is not ("http" or "https")) return;
        try
        {
            Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
