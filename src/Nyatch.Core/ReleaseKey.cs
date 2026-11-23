namespace Nyatch.Core;

/// <summary>
/// The public half of Nyatch's release signing key. Updates are installed only if their signature
/// checks out against it (see <see cref="ReleaseSignature"/>). Written by tools\ReleaseSigning.cs keygen.
/// </summary>
public static class ReleaseKey
{
    public const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPJyW8YgJ09GvptLRezPplJW0urmv
IYZbU1Wa2LE4hHrV2NPEQtI1Xx2d+0w7GNfNBPVL20148+cjHJKkoZ1RPg==
-----END PUBLIC KEY-----
""";
}
