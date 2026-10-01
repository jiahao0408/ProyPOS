using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Modules.CashRegister;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

public sealed class CashRegisterServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly CashRegisterService _cash;
    private readonly int _userId;

    public CashRegisterServiceTests()
    {
        _cash = new CashRegisterService(_db.Factory, _clock);
        _userId = new UserService(_db.Factory, _clock).CreateUser("Ana", "1234", Role.Cashier).Value!.Id;
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void StartsClosed()
    {
        Assert.False(_cash.IsOpen);
    }

    [Fact]
    public void Open_RecordsFloatUserAndTime()
    {
        var result = _cash.Open(_userId, 150m);

        Assert.True(result.Success);
        var session = _cash.GetOpenSession()!;
        Assert.Equal(150m, session.OpeningFloat);
        Assert.Equal(_userId, session.OpenedByUserId);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), session.OpenedAtUtc);
    }

    [Fact]
    public void Open_CannotOpenTwice()
    {
        _cash.Open(_userId, 100m);

        Assert.Equal("ErrorCashAlreadyOpen", _cash.Open(_userId, 50m).ErrorKey);
    }

    [Theory]
    [InlineData(-1, "ErrorFloatNegative")]
    [InlineData(10.555, "ErrorPriceDecimals")]
    public void Open_ValidatesFloat(decimal amount, string expectedError)
    {
        Assert.Equal(expectedError, _cash.Open(_userId, amount).ErrorKey);
    }
}
