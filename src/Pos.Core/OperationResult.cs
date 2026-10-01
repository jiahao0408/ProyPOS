namespace Pos.Core;

/// <summary>
/// Resultado de una operación de negocio. Los errores son claves de traducción,
/// para que la interfaz los muestre en el idioma del usuario.
/// </summary>
public record OperationResult(string? ErrorKey = null)
{
    public bool Success => ErrorKey is null;

    public static OperationResult Ok() => new();

    public static OperationResult Fail(string errorKey) => new(errorKey);
}

public sealed record OperationResult<T>(T? Value, string? ErrorKey = null) : OperationResult(ErrorKey)
{
    public static OperationResult<T> Ok(T value) => new(value);

    public static new OperationResult<T> Fail(string errorKey) => new(default, errorKey);
}
