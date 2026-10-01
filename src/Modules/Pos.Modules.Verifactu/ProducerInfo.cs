using System.Reflection;
using System.Text.Json;

namespace Pos.Modules.Verifactu;

/// <summary>
/// Datos del productor del sistema informático: van en cada registro (bloque SistemaInformatico)
/// y en la declaración responsable (VFA-06). Se leen de producer.json, junto al ejecutable,
/// para que el fabricante los complete sin recompilar.
/// </summary>
public sealed record ProducerInfo(
    string Name,
    string Nif,
    string Address,
    string SystemName,
    string SystemId,
    string Version,
    string DeclarationDate,
    string DeclarationPlace)
{
    public const string FileName = "producer.json";

    /// <summary>Los campos sin completar empiezan por "[" en producer.json.</summary>
    public bool IsComplete => !new[] { Name, Nif, Address, DeclarationDate, DeclarationPlace }.Any(v => v.Length == 0 || v.StartsWith('['));

    public static ProducerInfo Load(string? directory = null)
    {
        // La de StarSeaPOS (no la del proceso: en los tests es el lanzador de pruebas). Se fija al publicar con -p:Version.
        var version = typeof(ProducerInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
        var empty = new ProducerInfo("[COMPLETAR]", "[COMPLETAR]", "[COMPLETAR]", "StarSeaPOS", "SS", version, "[COMPLETAR]", "[COMPLETAR]");
        if (!File.Exists(path))
            return empty;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        string Get(string name, string fallback) =>
            doc.RootElement.TryGetProperty(name, out var p) && p.GetString() is { Length: > 0 } v ? v : fallback;

        return new ProducerInfo(
            Get("name", empty.Name),
            Get("nif", empty.Nif),
            Get("address", empty.Address),
            Get("systemName", empty.SystemName),
            Get("systemId", empty.SystemId),
            version,
            Get("declarationDate", empty.DeclarationDate),
            Get("declarationPlace", empty.DeclarationPlace));
    }
}
