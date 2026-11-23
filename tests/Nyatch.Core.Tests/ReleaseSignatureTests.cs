using System.Security.Cryptography;
using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class ReleaseSignatureTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void AcceptsOnlyTheRightKeyVersionAndFile()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfoPem();
        var version = new Version(1, 2, 0);
        var signature = ReleaseSignature.Sign(key, version, Hash);

        Assert.True(ReleaseSignature.Verify(publicKey, version, Hash, signature));
        Assert.True(ReleaseSignature.Verify(publicKey, version, Hash.ToUpperInvariant(), signature + "\n"));
        // Another version, another file, another key: refused.
        Assert.False(ReleaseSignature.Verify(publicKey, new Version(1, 2, 1), Hash, signature));
        Assert.False(ReleaseSignature.Verify(publicKey, version, new string('f', 64), signature));
        Assert.False(ReleaseSignature.Verify(publicKey, version, Hash, ReleaseSignature.Sign(other, version, Hash)));
        // Junk: refused, not thrown.
        Assert.False(ReleaseSignature.Verify(publicKey, version, Hash, "not base64!"));
        Assert.False(ReleaseSignature.Verify("not a key", version, Hash, signature));
    }

    [Fact]
    public void TheBuiltInKeyIsAPublicKey()
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(ReleaseKey.PublicKeyPem);
        Assert.Equal(256, key.KeySize);
        Assert.DoesNotContain("PRIVATE", ReleaseKey.PublicKeyPem);
    }
}
