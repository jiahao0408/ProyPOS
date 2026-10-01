namespace Pos.Core.Domain;

/// <summary>Ajuste de la app guardado en la BD como par clave-valor.</summary>
public class Setting
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}
