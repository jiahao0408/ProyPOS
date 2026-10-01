using Pos.Core.Security;

namespace Pos.Core.Tests;

public class PinHasherTests
{
    [Fact]
    public void Verify_AcceptsCorrectPin()
    {
        var hash = PinHasher.Hash("1234");

        Assert.True(PinHasher.Verify("1234", hash));
    }

    [Fact]
    public void Verify_RejectsWrongPin()
    {
        var hash = PinHasher.Hash("1234");

        Assert.False(PinHasher.Verify("4321", hash));
    }

    [Fact]
    public void Hash_UsesRandomSalt()
    {
        Assert.NotEqual(PinHasher.Hash("1234"), PinHasher.Hash("1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("١٢٣٤")] // dígitos no ASCII
    public void Hash_RejectsInvalidPins(string pin)
    {
        Assert.Throws<ArgumentException>(() => PinHasher.Hash(pin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2$100000$%%%$%%%")]
    [InlineData("md5$1$AAAA$AAAA")]
    public void Verify_ReturnsFalseForMalformedHash(string storedHash)
    {
        Assert.False(PinHasher.Verify("1234", storedHash));
    }
}
