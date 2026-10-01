using System.Globalization;
using Pos.Core.Localization;

namespace Pos.Localization;

/// <summary>
/// CFG-04: importes y fechas según la región. Los separadores decimales y de miles
/// siguen el idioma de la interfaz; la moneda (euro por defecto) y el formato de fecha
/// los fija el administrador.
/// </summary>
public sealed class RegionFormatter(ILocalizer localizer)
{
    public const string DefaultCurrencySymbol = "€";

    /// <summary>Formatos de fecha que puede elegir el admin; cadena vacía = el del idioma.</summary>
    public static IReadOnlyList<string> DateFormats { get; } = ["", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd.MM.yyyy"];

    public string CurrencySymbol { get; set; } = DefaultCurrencySymbol;

    public string DateFormat { get; set; } = "";

    public NumberFormatInfo NumberFormat
    {
        get
        {
            var format = (NumberFormatInfo)localizer.Culture.NumberFormat.Clone();
            format.CurrencySymbol = CurrencySymbol;
            format.CurrencyDecimalDigits = 2;
            return format;
        }
    }

    public string FormatMoney(decimal amount) => amount.ToString("C", NumberFormat);

    /// <summary>Número con 2 decimales sin símbolo de moneda, para campos editables.</summary>
    public string FormatAmount(decimal amount) => amount.ToString("N2", NumberFormat);

    public string FormatDate(DateTime date) =>
        date.ToString(DateFormat.Length == 0 ? localizer.Culture.DateTimeFormat.ShortDatePattern : DateFormat, localizer.Culture);

    public string FormatDateTime(DateTime date) =>
        $"{FormatDate(date)} {date.ToString(localizer.Culture.DateTimeFormat.ShortTimePattern, localizer.Culture)}";

    /// <summary>
    /// Lee un importe tecleado por el usuario. Acepta el separador decimal del idioma y,
    /// por comodidad, también el punto o la coma si no hay ambigüedad.
    /// </summary>
    public bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().Replace(CurrencySymbol, "").Trim();

        // Un solo separador seguido de 1 o 2 cifras es siempre decimal, sea punto o coma:
        // un cajero que teclea "12.50" en español quiere 12,50 €, no 1.250 €.
        var separatorIndex = text.IndexOfAny(['.', ',']);
        if (separatorIndex >= 0 && separatorIndex == text.LastIndexOfAny(['.', ',']) && text.Length - separatorIndex - 1 is 1 or 2)
        {
            var normalized = text.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
        }

        // Resto de casos ("1.234,50", "1234"): reglas del idioma.
        return decimal.TryParse(text, NumberStyles.Number, NumberFormat, out amount);
    }
}
