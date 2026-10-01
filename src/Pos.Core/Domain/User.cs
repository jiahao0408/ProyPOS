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

    /// <summary>Intentos fallidos seguidos; al llegar a 5 se bloquea (USR-01).</summary>
    public int FailedPinAttempts { get; set; }

    public bool IsLocked { get; set; }

    public bool IsActive { get; set; } = true;
}
