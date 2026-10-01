using System.Collections.Concurrent;
using Pos.Core.Localization;

namespace Pos.Localization;

/// <summary>
/// Idioma de lo que se imprime (tickets, facturas en PDF, cierre Z), independiente del idioma de la
/// aplicación: el cajero puede trabajar en chino e imprimir los tickets en español.
/// Sin idioma de impresión configurado se imprime en el idioma de la aplicación.
/// La moneda y el formato de fecha son los mismos que en la aplicación.
/// </summary>
public sealed class PrintLocalization(ILocalizer appLocalizer, RegionFormatter appFormatter)
{
    private readonly ConcurrentDictionary<string, JsonLocalizer> _localizers = new();

    /// <summary>Código del idioma de impresión; null = el de la aplicación.</summary>
    public string? Language { get; set; }

    public string CurrentLanguage => Language ?? appLocalizer.CurrentLanguage;

    /// <summary>Textos y formato para imprimir en <paramref name="language"/> (o en el idioma de impresión).</summary>
    public (ILocalizer L, RegionFormatter Formatter) For(string? language = null)
    {
        language ??= CurrentLanguage;
        if (language == appLocalizer.CurrentLanguage || appLocalizer is not JsonLocalizer json
            || json.AvailableLanguages.All(l => l.Code != language))
            return (appLocalizer, appFormatter);

        var localizer = _localizers.GetOrAdd(language, code =>
        {
            var l = new JsonLocalizer(json.LocalesDirectory, json.DefaultLanguage);
            l.SetLanguage(code);
            return l;
        });
        return (localizer, new RegionFormatter(localizer)
        {
            CurrencySymbol = appFormatter.CurrencySymbol,
            DateFormat = appFormatter.DateFormat,
        });
    }
}
