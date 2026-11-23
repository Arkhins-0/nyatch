using System.Security.Cryptography;
using System.Text;

namespace Nyatch.Core;

/// <summary>
/// Release signatures. Each release's installer comes with a ".sig" file: an ECDSA P-256 signature, made
/// with a private key only Arkhins holds, over the release's version and the installer's SHA-256. Nyatch
/// carries the matching public key (<see cref="ReleaseKey"/>) and installs an update only if the signature
/// checks out, so a release cannot be faked even by someone who gets into the GitHub account.
/// </summary>
public static class ReleaseSignature
{
    /// <summary>What is signed: the version and the installer's SHA-256, so a signature fits one file of one version.</summary>
    public static string Message(Version version, string sha256Hex) =>
        $"Nyatch {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}\nsha256 {sha256Hex.Trim().ToLowerInvariant()}\n";

    public static string Sign(ECDsa key, Version version, string sha256Hex) =>
        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(Message(version, sha256Hex)), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    /// <summary>True only if <paramref name="signature"/> (base64, as in a .sig file) was made by the private key of <paramref name="publicKeyPem"/> for exactly this version and hash.</summary>
    public static bool Verify(string publicKeyPem, Version version, string sha256Hex, string signature)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        using var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(publicKeyPem);
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            return false;
        }
        return key.VerifyData(Encoding.UTF8.GetBytes(Message(version, sha256Hex)), bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
