using KameronSushi.Infrastructure.Auth;
using Xunit;

namespace KameronSushi.Tests;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_And_Verify_AcceptsTheOriginalPin()
    {
        var hash = _hasher.Hash("4821");

        Assert.True(_hasher.Verify("4821", hash));
        Assert.False(_hasher.Verify("4822", hash));
    }

    [Fact]
    public void Hash_UsesARandomSalt()
    {
        Assert.NotEqual(_hasher.Hash("4821"), _hasher.Hash("4821"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    public void Hash_RejectsAnInvalidPin(string pin)
    {
        var error = Assert.Throws<ArgumentException>(() => _hasher.Hash(pin));
        Assert.Contains("4 números", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$600000$invalid$invalid")]
    public void Verify_RejectsMalformedHashes(string hash)
    {
        Assert.False(_hasher.Verify("4821", hash));
    }
}
