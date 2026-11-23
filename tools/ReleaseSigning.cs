#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:package System.Security.Cryptography.ProtectedData@9.0.0
#:project ../src/Nyatch.Core/Nyatch.Core.csproj

// Nyatch's release signing key: make it, sign installers with it, back it up and restore it.
//
//   dotnet run tools\ReleaseSigning.cs -- keygen <path to ReleaseKey.cs>   make the key (once)
//   dotnet run tools\ReleaseSigning.cs -- sign <installer.msi> <version>   write <installer.msi>.sig
//   dotnet run tools\ReleaseSigning.cs -- backup <file>                     a passphrase-protected copy
//   dotnet run tools\ReleaseSigning.cs -- restore <file>                    bring a backup onto this PC
//   dotnet run tools\ReleaseSigning.cs -- public                            print the public key
//
// The private key never goes into the project. It is kept in %LOCALAPPDATA%\Nyatch Release Signing,
// encrypted with Windows' protection for this user (DPAPI), so only this Windows account can use it.
// Keep a backup somewhere safe (a password manager): without the key, no new version can be signed,
// and installed copies of Nyatch would refuse every update.

using System.Security.Cryptography;
using System.Text;
using Nyatch.Core;

var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch Release Signing");
var keyFile = Path.Combine(folder, "signing-key.dpapi");
var entropy = Encoding.UTF8.GetBytes("Nyatch release signing key");

int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

ECDsa Load()
{
    if (!File.Exists(keyFile)) throw new InvalidOperationException($"No signing key on this PC ({keyFile}). Run keygen, or restore a backup.");
    var pkcs8 = ProtectedData.Unprotect(File.ReadAllBytes(keyFile), entropy, DataProtectionScope.CurrentUser);
    var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(pkcs8, out _);
    CryptographicOperations.ZeroMemory(pkcs8);
    return key;
}

void Store(ECDsa key)
{
    Directory.CreateDirectory(folder);
    var pkcs8 = key.ExportPkcs8PrivateKey();
    File.WriteAllBytes(keyFile, ProtectedData.Protect(pkcs8, entropy, DataProtectionScope.CurrentUser));
    CryptographicOperations.ZeroMemory(pkcs8);
}

string Passphrase(string prompt)
{
    var fromEnvironment = Environment.GetEnvironmentVariable("NYATCH_KEY_PASSPHRASE");
    if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;
    Console.Write(prompt);
    var typed = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (typed.Length > 0) typed.Length--; continue; }
        typed.Append(key.KeyChar);
    }
    Console.WriteLine();
    return typed.ToString();
}

try
{
    switch (args.FirstOrDefault())
    {
        case "keygen":
        {
            if (args.Length < 2) return Fail("keygen <path to ReleaseKey.cs> [--force]");
            if (File.Exists(keyFile) && !args.Contains("--force"))
            {
                return Fail($"A signing key already exists ({keyFile}). Replacing it means installed copies refuse updates signed with the new one; add --force only if that is meant.");
            }
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Store(key);
            var publicPem = key.ExportSubjectPublicKeyInfoPem();
            File.WriteAllText(args[1], $$""""
                namespace Nyatch.Core;

                /// <summary>
                /// The public half of Nyatch's release signing key. Updates are installed only if their signature
                /// checks out against it (see <see cref="ReleaseSignature"/>). Written by tools\ReleaseSigning.cs keygen.
                /// </summary>
                public static class ReleaseKey
                {
                    public const string PublicKeyPem = """
                {{publicPem}}
                """;
                }

                """", new UTF8Encoding(false));
            Console.WriteLine($"Signing key made and stored for this Windows user: {keyFile}");
            Console.WriteLine($"Public key written to {args[1]}");
            Console.WriteLine("Back it up now: dotnet run tools\\ReleaseSigning.cs -- backup <file>");
            return 0;
        }
        case "sign":
        {
            if (args.Length < 3) return Fail("sign <installer.msi> <version>");
            var version = Updates.ParseVersion(args[2]) ?? throw new ArgumentException($"Not a version: {args[2]}");
            string hash;
            using (var file = File.OpenRead(args[1])) hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            using var key = Load();
            var signature = ReleaseSignature.Sign(key, version, hash);
            if (!ReleaseSignature.Verify(ReleaseKey.PublicKeyPem, version, hash, signature))
            {
                return Fail("The key on this PC does not match the public key built into Nyatch (src\\Nyatch.Core\\ReleaseKey.cs). Nothing was signed.");
            }
            File.WriteAllText(args[1] + ".sig", signature + "\n", new UTF8Encoding(false));
            Console.WriteLine($"Signed {Path.GetFileName(args[1])} as version {version}: {args[1]}.sig");
            return 0;
        }
        case "backup":
        {
            if (args.Length < 2) return Fail("backup <file>");
            using var key = Load();
            var passphrase = Passphrase("Passphrase for the backup: ");
            if (passphrase.Length < 12) return Fail("Use a passphrase of at least 12 characters.");
            if (Environment.GetEnvironmentVariable("NYATCH_KEY_PASSPHRASE") is null && Passphrase("The same again: ") != passphrase) return Fail("The two passphrases differ. Nothing was written.");
            var pem = key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
            File.WriteAllText(args[1], pem + "\n", new UTF8Encoding(false));
            Console.WriteLine($"Backup written to {args[1]}. Keep it and its passphrase somewhere safe, apart from each other.");
            return 0;
        }
        case "restore":
        {
            if (args.Length < 2) return Fail("restore <file>");
            if (File.Exists(keyFile) && !args.Contains("--force")) return Fail($"A signing key already exists ({keyFile}); add --force to replace it.");
            using var key = ECDsa.Create();
            key.ImportFromEncryptedPem(File.ReadAllText(args[1]), Passphrase("Passphrase of the backup: "));
            Store(key);
            Console.WriteLine($"Signing key restored for this Windows user: {keyFile}");
            return 0;
        }
        case "public":
        {
            using var key = Load();
            Console.WriteLine(key.ExportSubjectPublicKeyInfoPem());
            return 0;
        }
        default:
            return Fail("keygen | sign | backup | restore | public (see the top of tools\\ReleaseSigning.cs)");
    }
}
catch (Exception e) when (e is CryptographicException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
{
    return Fail(e.Message);
}
