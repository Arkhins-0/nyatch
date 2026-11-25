using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Nyatch.Core;

/// <summary>A key the user can connect.</summary>
public sealed record SecretInfo(string Id, string Name, string What, string Label, string Placeholder, string Help);

/// <summary>
/// API keys, kept in Windows Credential Manager. A key is written and deleted
/// from the settings window and read only when its service is called; nothing
/// shows it again once saved.
/// </summary>
public static class Secrets
{
    public const string Anthropic = "anthropic-key";
    public const string OpenAi = "openai-key";
    public const string Gemini = "gemini-key";
    public const string DeepSeek = "deepseek-key";
    public const string Mistral = "mistral-key";
    public const string Xai = "xai-key";
    public const string Groq = "groq-key";
    public const string Perplexity = "perplexity-key";
    public const string OpenRouter = "openrouter-key";


    /// <summary>The secrets the app knows about. Anything else is refused.</summary>
    public static readonly IReadOnlyList<SecretInfo> Known =
    [
        new(Anthropic, "Claude", "Its chat in the LLM panel.", "Anthropic API key", "sk-ant-…",
            "Create a key at platform.claude.com › API keys. This is an API account billed by use; a Claude.ai subscription does not include it."),
        new(OpenAi, "ChatGPT", "Its chat in the LLM panel.", "OpenAI API key", "sk-…",
            "Create a key at platform.openai.com › API keys. This is an API account billed by use; a ChatGPT subscription does not include it."),
        new(Gemini, "Gemini", "Its chat in the LLM panel.", "Gemini API key", "AIza…",
            "Create a key at aistudio.google.com › Get API key."),
        new(DeepSeek, "DeepSeek", "Its chat in the LLM panel.", "DeepSeek API key", "sk-…",
            "Create a key at platform.deepseek.com › API keys."),
        new(Mistral, "Mistral", "Its chat in the LLM panel.", "Mistral API key", "",
            "Create a key at console.mistral.ai › API keys."),
        new(Xai, "Grok", "Its chat in the LLM panel.", "xAI API key", "xai-…",
            "Create a key at console.x.ai › API keys."),
        new(Groq, "Groq", "Its chat in the LLM panel.", "Groq API key", "gsk_…",
            "Create a key at console.groq.com › API keys. Groq runs open models (Llama, Qwen and others) very quickly."),
        new(Perplexity, "Perplexity", "Its chat in the LLM panel.", "Perplexity API key", "pplx-…",
            "Create a key at perplexity.ai › Settings › API. Its answers search the web."),
        new(OpenRouter, "OpenRouter", "Its chat in the LLM panel.", "OpenRouter API key", "sk-or-…",
            "Create a key at openrouter.ai › Keys. One key reaches hundreds of models; set one as the model, like meta-llama/llama-3.3-70b-instruct."),
    ];

    /// <summary>Longest secret accepted (Credential Manager's own limit is 2560 bytes).</summary>
    public const int MaxBytes = 2400;

    private static string Target(string id) => $"Nyatch/{id}";

    private static bool IsKnown(string id) => Known.Any(s => s.Id == id);

    /// <summary>The stored secret, or null if it is not set.</summary>
    public static string? Get(string id)
    {
        if (!IsKnown(id) || !CredRead(Target(id), CredTypeGeneric, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            // Cleaned as it is used too, so a key kept before cleaning existed still works.
            var value = Clean(Encoding.UTF8.GetString(bytes));
            return value.Length == 0 ? null : value;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static bool Has(string id) => Get(id) is not null;

    /// <summary>
    /// A key as pasted, without what a copy can carry along: spaces and line breaks, invisible characters (a
    /// zero-width space, a non-breaking space, a byte-order mark) and quotes around it. A key is plain ASCII,
    /// and a request header must be (anything else made it fail: "headers must contain only ASCII characters").
    /// </summary>
    public static string Clean(string value) =>
        new string(value.Where(c => c > ' ' && c < 0x7F && c is not ('"' or '\'' or '`')).ToArray());

    /// <summary>Store a secret. Throws <see cref="ArgumentException"/> with a message for the user if it cannot be kept.</summary>
    public static void Set(string id, string value)
    {
        if (!IsKnown(id)) throw new ArgumentException("Unknown key.");
        // A letter or digit that is not plain ASCII is not part of any key: it was copied wrong.
        if (value.Any(c => c > 0x7F && char.IsLetterOrDigit(c))) throw new ArgumentException("That key has a letter that is not part of a key. Copy it again from the service's page.");
        value = Clean(value);
        if (value.Length == 0) throw new ArgumentException("Nothing to save.");
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxBytes) throw new ArgumentException("That is too long to store.");

        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(id),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "nyatch",
            };
            if (!CredWrite(ref credential, 0)) throw new ArgumentException("Windows refused to store it: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    /// <summary>Delete a secret. Deleting one that is not there is fine.</summary>
    public static void Delete(string id)
    {
        if (IsKnown(id)) CredDelete(Target(id), CredTypeGeneric, 0);
    }

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
