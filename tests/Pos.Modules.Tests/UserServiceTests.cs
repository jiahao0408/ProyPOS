using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

public sealed class UserServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly UserService _users;

    public UserServiceTests() => _users = new UserService(_db.Factory, _clock);

    public void Dispose() => _db.Dispose();

    [Fact]
    public void CreateFirstAdmin_OnlyWorksOnEmptyDatabase()
    {
        Assert.False(_users.HasAnyUser());

        var first = _users.CreateFirstAdmin("Ana", "1234");
        var second = _users.CreateFirstAdmin("Luis", "5678");

        Assert.True(first.Success);
        Assert.Equal(Role.Admin, first.Value!.Role);
        Assert.Equal("ErrorUsersAlreadyExist", second.ErrorKey);
    }

    [Theory]
    [InlineData("", "1234", "ErrorNameRequired")]
    [InlineData("Ana", "12", "ErrorPinFormat")]
    [InlineData("Ana", "abcd", "ErrorPinFormat")]
    public void CreateUser_ValidatesInput(string name, string pin, string expectedError)
    {
        Assert.Equal(expectedError, _users.CreateUser(name, pin, Role.Cashier).ErrorKey);
    }

    [Fact]
    public void CreateUser_RejectsDuplicateName()
    {
        _users.CreateUser("Ana", "1234", Role.Admin);

        Assert.Equal("ErrorUserNameTaken", _users.CreateUser(" Ana ", "5678", Role.Cashier).ErrorKey);
    }

    [Fact]
    public void SignIn_WithCorrectPin_Succeeds()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;

        var result = _users.SignIn(user.Id, "1234");

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal("Ana", result.User!.Name);
    }

    [Fact]
    public void SignIn_CountsDownRemainingAttempts()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;

        var result = _users.SignIn(user.Id, "0000");

        Assert.Equal(LoginStatus.WrongPin, result.Status);
        Assert.Equal(4, result.RemainingAttempts);
    }

    [Fact]
    public void SignIn_LocksAfterFiveFailures_EvenWithCorrectPin()
    {
        // USR-01: bloqueo tras 5 intentos fallidos.
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;
        for (var i = 0; i < 4; i++)
            Assert.Equal(LoginStatus.WrongPin, _users.SignIn(user.Id, "0000").Status);

        Assert.Equal(LoginStatus.Locked, _users.SignIn(user.Id, "0000").Status);
        Assert.Equal(LoginStatus.Locked, _users.SignIn(user.Id, "1234").Status);
    }

    [Fact]
    public void SignIn_LockExpiresAfterLockoutDuration()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;
        for (var i = 0; i < 5; i++)
            _users.SignIn(user.Id, "0000");

        _clock.Advance(UserService.LockoutDuration + TimeSpan.FromSeconds(1));

        Assert.Equal(LoginStatus.Success, _users.SignIn(user.Id, "1234").Status);
    }

    [Fact]
    public void SignIn_AfterExpiredLock_StartsCountingFromZero()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;
        for (var i = 0; i < 5; i++)
            _users.SignIn(user.Id, "0000");
        _clock.Advance(UserService.LockoutDuration + TimeSpan.FromSeconds(1));

        var result = _users.SignIn(user.Id, "0000");

        Assert.Equal(LoginStatus.WrongPin, result.Status);
        Assert.Equal(4, result.RemainingAttempts);
    }

    [Fact]
    public void SignIn_SuccessResetsFailedAttempts()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;
        for (var i = 0; i < 4; i++)
            _users.SignIn(user.Id, "0000");

        _users.SignIn(user.Id, "1234");

        Assert.Equal(4, _users.SignIn(user.Id, "0000").RemainingAttempts);
    }

    [Fact]
    public void Unlock_LetsTheUserSignInAgain()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;
        for (var i = 0; i < 5; i++)
            _users.SignIn(user.Id, "0000");

        _users.Unlock(user.Id);

        Assert.Equal(LoginStatus.Success, _users.SignIn(user.Id, "1234").Status);
    }

    [Fact]
    public void SignIn_InactiveUser_IsNotFound()
    {
        _users.CreateUser("Admin", "9999", Role.Admin);
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;
        _users.UpdateUser(user.Id, "Ana", Role.Cashier, isActive: false, newPin: null);

        Assert.Equal(LoginStatus.UserNotFound, _users.SignIn(user.Id, "1234").Status);
    }

    [Fact]
    public void VerifyAdminPin_OnlyAcceptsAdmins()
    {
        // USR-02: las acciones de admin piden PIN de admin al cajero.
        _users.CreateUser("Admin", "9999", Role.Admin);
        _users.CreateUser("Cajero", "1111", Role.Cashier);

        Assert.Equal("Admin", _users.VerifyAdminPin("9999")?.Name);
        Assert.Null(_users.VerifyAdminPin("1111"));
        Assert.Null(_users.VerifyAdminPin("0000"));
    }

    [Fact]
    public void UpdateUser_CannotRemoveTheLastAdmin()
    {
        var admin = _users.CreateUser("Admin", "9999", Role.Admin).Value!;

        Assert.Equal("ErrorLastAdmin", _users.UpdateUser(admin.Id, "Admin", Role.Cashier, true, null).ErrorKey);
        Assert.Equal("ErrorLastAdmin", _users.UpdateUser(admin.Id, "Admin", Role.Admin, false, null).ErrorKey);
    }

    [Fact]
    public void UpdateUser_ChangesPin()
    {
        var user = _users.CreateUser("Ana", "1234", Role.Cashier).Value!;

        _users.UpdateUser(user.Id, "Ana", Role.Cashier, true, "4321");

        Assert.Equal(LoginStatus.WrongPin, _users.SignIn(user.Id, "1234").Status);
        Assert.Equal(LoginStatus.Success, _users.SignIn(user.Id, "4321").Status);
    }
}
