using System.ComponentModel;
using System.Globalization;

namespace Pos.Core.Localization;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Acceso a los textos traducidos. Notifica el cambio del indexador ("Item[]")
/// para que la interfaz se actualice sin reiniciar (CFG-01).
/// </summary>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>Texto traducido; si falta en el idioma actual usa el idioma por defecto y, si no, devuelve la clave.</summary>
    string this[string key] { get; }

    string CurrentLanguage { get; }

    CultureInfo Culture { get; }

    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    void SetLanguage(string code);

    event EventHandler? LanguageChanged;
}
