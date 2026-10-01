namespace Pos.Core.Domain;

public enum Role
{
    Cashier = 0,
    Admin = 1,
}

public class User
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Hash del PIN generado por <see cref="Security.PinHasher"/>; nunca el PIN en claro.</summary>
    public required string PinHash { get; set; }

    public Role Role { get; set; }

    /// <summary>Idioma preferido del usuario (CFG-02). Null = idioma general de la app.</summary>
    public string? LanguageCode { get; set; }

    /// <summary>Intentos fallidos seguidos; al llegar al máximo se bloquea (USR-01).</summary>
    public int FailedPinAttempts { get; set; }

    /// <summary>
    /// Bloqueado hasta esta hora (UTC). El bloqueo es temporal para que el único admin
    /// no pueda quedarse fuera para siempre; un admin puede desbloquear antes.
    /// </summary>
    public DateTime? LockedUntilUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsAdmin => Role == Role.Admin;

    public bool IsLockedAt(DateTime utcNow) => LockedUntilUtc is { } until && until > utcNow;
}
