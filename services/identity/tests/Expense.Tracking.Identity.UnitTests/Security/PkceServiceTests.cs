using System.Security.Cryptography;
using System.Text;
using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.UnitTests.Security;

public sealed class PkceServiceTests
{
    private readonly PkceService _service = new();

    private static string ChallengeS256(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncoder.Encode(hash);
    }

    [Fact]
    public void Verify_S256_WithMatchingVerifier_ReturnsTrue()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = ChallengeS256(verifier);

        Assert.True(_service.Verify(verifier, challenge, CodeChallengeMethod.S256));
    }

    [Fact]
    public void Verify_S256_WithWrongVerifier_ReturnsFalse()
    {
        var challenge = ChallengeS256("the-real-verifier");

        Assert.False(_service.Verify("another-verifier", challenge, CodeChallengeMethod.S256));
    }

    [Fact]
    public void Verify_Plain_ComparesDirectly()
    {
        Assert.True(_service.Verify("abc123", "abc123", CodeChallengeMethod.Plain));
        Assert.False(_service.Verify("abc123", "different", CodeChallengeMethod.Plain));
    }

    [Fact]
    public void Verify_WithUnknownMethod_ReturnsFalse()
    {
        Assert.False(_service.Verify("abc", "abc", "MD5"));
    }
}
