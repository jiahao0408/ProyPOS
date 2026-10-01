using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Security;
using Pos.Data;

namespace Pos.Modules.Users;

public enum LoginStatus
{
    Success,
    WrongPin,
    Locked,
    UserNotFound,
}

public sealed record LoginResult(LoginStatus Status, User? User = null, int RemainingAttempts = 0, DateTime? LockedUntilUtc = null);

/// <summary>Alta de usuarios y entrada con PIN (USR-01, USR-02).</summary>
public sealed class UserService(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public bool HasAnyUser()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Users.Any();
    }

    public IReadOnlyList<User> GetUsers(bool includeInactive = false)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Users.AsNoTracking()
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.Name)
            .ToList();
    }

    /// <summary>Primer arranque: crea el administrador inicial. Solo funciona si no hay ningún usuario.</summary>
    public OperationResult<User> CreateFirstAdmin(string name, string pin)
    {
        if (HasAnyUser())
            return OperationResult<User>.Fail("ErrorUsersAlreadyExist");
        return CreateUser(name, pin, Role.Admin);
    }

    public OperationResult<User> CreateUser(string name, string pin, Role role)
    {
        name = name.Trim();
        if (name.Length == 0)
            return OperationResult<User>.Fail("ErrorNameRequired");
        if (!PinHasher.IsValidPin(pin))
            return OperationResult<User>.Fail("ErrorPinFormat");

        using var db = dbFactory.CreateDbContext();
        if (db.Users.Any(u => u.Name == name))
            return OperationResult<User>.Fail("ErrorUserNameTaken");

        var user = new User { Name = name, PinHash = PinHasher.Hash(pin), Role = role };
        db.Users.Add(user);
        db.SaveChanges();
        return OperationResult<User>.Ok(user);
    }

    /// <summary>Cambia nombre, rol, estado y, si se indica, el PIN.</summary>
    public OperationResult UpdateUser(int userId, string name, Role role, bool isActive, string? newPin)
    {
        name = name.Trim();
        if (name.Length == 0)
            return OperationResult.Fail("ErrorNameRequired");
        if (!string.IsNullOrEmpty(newPin) && !PinHasher.IsValidPin(newPin))
            return OperationResult.Fail("ErrorPinFormat");

        using var db = dbFactory.CreateDbContext();
        var user = db.Users.Find(userId);
        if (user is null)
            return OperationResult.Fail("ErrorUserNotFound");
        if (db.Users.Any(u => u.Name == name && u.Id != userId))
            return OperationResult.Fail("ErrorUserNameTaken");

        var losesAdmin = user.IsAdmin && (role != Role.Admin || !isActive);
        if (losesAdmin && !db.Users.Any(u => u.Id != userId && u.Role == Role.Admin && u.IsActive))
            return OperationResult.Fail("ErrorLastAdmin");

        user.Name = name;
        user.Role = role;
        user.IsActive = isActive;
        if (!string.IsNullOrEmpty(newPin))
        {
            user.PinHash = PinHasher.Hash(newPin);
            user.FailedPinAttempts = 0;
            user.LockedUntilUtc = null;
        }
        db.SaveChanges();
        return OperationResult.Ok();
    }

    public void Unlock(int userId)
    {
        using var db = dbFactory.CreateDbContext();
        var user = db.Users.Find(userId);
        if (user is null)
            return;
        user.FailedPinAttempts = 0;
        user.LockedUntilUtc = null;
        db.SaveChanges();
    }

    /// <summary>USR-01: PIN de 4 dígitos; tras 5 fallos seguidos el usuario queda bloqueado un tiempo.</summary>
    public LoginResult SignIn(int userId, string pin)
    {
        using var db = dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId && u.IsActive);
        if (user is null)
            return new LoginResult(LoginStatus.UserNotFound);

        var now = clock.GetUtcNow().UtcDateTime;
        if (user.IsLockedAt(now))
            return new LoginResult(LoginStatus.Locked, LockedUntilUtc: user.LockedUntilUtc);

        if (PinHasher.Verify(pin, user.PinHash))
        {
            user.FailedPinAttempts = 0;
            user.LockedUntilUtc = null;
            db.SaveChanges();
            return new LoginResult(LoginStatus.Success, user);
        }

        // Un bloqueo ya vencido empieza de cero.
        if (user.LockedUntilUtc is not null)
        {
            user.LockedUntilUtc = null;
            user.FailedPinAttempts = 0;
        }

        user.FailedPinAttempts++;
        if (user.FailedPinAttempts >= MaxFailedAttempts)
        {
            user.LockedUntilUtc = now + LockoutDuration;
            db.SaveChanges();
            return new LoginResult(LoginStatus.Locked, LockedUntilUtc: user.LockedUntilUtc);
        }

        db.SaveChanges();
        return new LoginResult(LoginStatus.WrongPin, RemainingAttempts: MaxFailedAttempts - user.FailedPinAttempts);
    }

    /// <summary>USR-02: comprueba el PIN de cualquier administrador activo, para autorizar una acción de admin.</summary>
    public User? VerifyAdminPin(string pin)
    {
        if (!PinHasher.IsValidPin(pin))
            return null;

        var now = clock.GetUtcNow().UtcDateTime;
        using var db = dbFactory.CreateDbContext();
        return db.Users.AsNoTracking()
            .Where(u => u.Role == Role.Admin && u.IsActive)
            .AsEnumerable()
            .FirstOrDefault(u => !u.IsLockedAt(now) && PinHasher.Verify(pin, u.PinHash));
    }
}
