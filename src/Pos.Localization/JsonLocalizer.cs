using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Pos.Core.Localization;

namespace Pos.Localization;

/// <summary>
/// Carga las traducciones de "&lt;carpeta&gt;/&lt;código&gt;.json" (UTF-8, diccionario plano clave → texto).
/// Añadir un idioma = copiar un fichero nuevo en la carpeta, sin recompilar (CFG-05).
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    public const string LanguageNameKey = "LanguageName";

    private readonly string _directory;
    private readonly Dictionary<string, string> _fallback;
    private Dictionary<string, string> _current;

    public JsonLocalizer(string directory, string defaultLanguage = "es")
    {
        _directory = directory;
        DefaultLanguage = defaultLanguage;
        _fallback = Load(defaultLanguage);
        _current = _fallback;
        CurrentLanguage = defaultLanguage;
        AvailableLanguages = Directory.EnumerateFiles(directory, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Select(code => new LanguageInfo(code, Load(code).GetValueOrDefault(LanguageNameKey, code)))
            .OrderBy(l => l.Code, StringComparer.Ordinal)
            .ToList();
    }

    public string DefaultLanguage { get; }

    /// <summary>Carpeta de las traducciones (para crear otro localizador, p. ej. el de impresión).</summary>
    public string LocalesDirectory => _directory;

    public string CurrentLanguage { get; private set; }

    public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLanguage);

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    public string this[string key] =>
        _current.TryGetValue(key, out var text) || _fallback.TryGetValue(key, out text) ? text : key;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string code)
    {
        if (code == CurrentLanguage)
            return;

        _current = code == DefaultLanguage ? _fallback : Load(code);
        CurrentLanguage = code;

        // "Item[]" es la convención de WPF; "Item" es la que escuchan los bindings compilados de Avalonia.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, string> Load(string code)
    {
        var path = Path.Combine(_directory, code + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"No existe el fichero de idioma '{code}'.", path);

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? throw new InvalidDataException($"El fichero de idioma '{path}' está vacío.");
    }
}
