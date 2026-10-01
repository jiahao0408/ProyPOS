using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Modules;
using Pos.Data;
using Pos.Localization;

namespace Pos.App.ViewModels;

public sealed record DateFormatOption(string Format, string Title);

/// <summary>CFG-01 (idioma de la interfaz), idioma de impresión de los tickets, CFG-04 (moneda y formatos), más la inactividad (USR-01).</summary>
public partial class SettingsPageViewModel(
    ILocalizer localizer,
    SettingsStore settings,
    RegionFormatter formatter,
    PrintLocalization print,
    TimeProvider clock,
    IEnumerable<IModule> modules) : PageViewModel(localizer)
{
    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    /// <summary>Idioma de los tickets impresos; código vacío = el de la aplicación.</summary>
    [ObservableProperty]
    private LanguageInfo? _selectedPrintLanguage;

    [ObservableProperty]
    private IReadOnlyList<LanguageInfo> _printLanguages = [];

    [ObservableProperty]
    private string _currencySymbol = "";

    [ObservableProperty]
    private DateFormatOption? _selectedDateFormat;

    [ObservableProperty]
    private string _inactivityMinutesText = "";

    /// <summary>VEN-05: descuento máximo (%) que puede aplicar un cajero sin PIN de admin.</summary>
    [ObservableProperty]
    private string _maxCashierDiscountText = "";

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private IReadOnlyList<DateFormatOption> _dateFormats = [];

    [ObservableProperty]
    private IReadOnlyList<string> _moduleNames = [];

    public IReadOnlyList<LanguageInfo> Languages => L.AvailableLanguages;

    public override void Load()
    {
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == L.CurrentLanguage);
        CurrencySymbol = formatter.CurrencySymbol;
        InactivityMinutesText = settings.GetInt(SettingKeys.InactivityMinutes, ShellViewModel.DefaultInactivityMinutes).ToString(L.Culture);
        MaxCashierDiscountText = (decimal.TryParse(settings.Get(Pos.Modules.Sales.SalesSettingKeys.MaxCashierDiscount), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var max) ? max : Pos.Modules.Sales.SalesSettingKeys.DefaultMaxCashierDiscount).ToString("0.##", L.Culture);
        RefreshTexts(formatter.DateFormat);
        SelectedPrintLanguage = PrintLanguages.FirstOrDefault(l => l.Code == (print.Language ?? ""));
    }

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(InactivityMinutesText, out var minutes) || minutes < 0)
        {
            ShowError("ErrorNumberFormat");
            return;
        }
        if (!formatter.TryParseAmount(MaxCashierDiscountText.Replace("%", ""), out var maxDiscount) || maxDiscount is < 0 or > 100)
        {
            ShowError("ErrorNumberFormat");
            return;
        }
        var symbol = CurrencySymbol.Trim();
        if (symbol.Length == 0)
        {
            ShowError("ErrorCurrencyRequired");
            return;
        }

        var dateFormat = SelectedDateFormat?.Format ?? "";
        settings.Set(SettingKeys.CurrencySymbol, symbol);
        settings.Set(SettingKeys.DateFormat, dateFormat);
        settings.Set(SettingKeys.InactivityMinutes, minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        settings.Set(Pos.Modules.Sales.SalesSettingKeys.MaxCashierDiscount, maxDiscount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        formatter.CurrencySymbol = symbol;
        formatter.DateFormat = dateFormat;

        if (SelectedLanguage is { } language)
        {
            settings.Set(SettingKeys.Language, language.Code);
            L.SetLanguage(language.Code); // CFG-01: cambia todos los textos sin reiniciar
        }

        var printLanguage = SelectedPrintLanguage?.Code ?? "";
        settings.Set(SettingKeys.PrintLanguage, printLanguage);
        print.Language = printLanguage.Length == 0 ? null : printLanguage;

        RefreshTexts(dateFormat);
        SelectedPrintLanguage = PrintLanguages.FirstOrDefault(l => l.Code == printLanguage);
        ShowInfo("Saved");
    }

    /// <summary>Textos que no salen de un binding a L[...] y hay que rehacer al cambiar de idioma.</summary>
    private void RefreshTexts(string dateFormat)
    {
        DateFormats = RegionFormatter.DateFormats
            .Select(f => new DateFormatOption(f, f.Length == 0 ? L["DateFormatLanguage"] : f))
            .ToList();
        SelectedDateFormat = DateFormats.FirstOrDefault(f => f.Format == dateFormat) ?? DateFormats[0];
        PrintLanguages = [new LanguageInfo("", L["PrintLanguageSameAsApp"]), .. Languages];
        ModuleNames = modules.Select(m => $"{m.Id} · {L[m.NameKey]}").ToList();
        Preview = $"{formatter.FormatMoney(1234.5m)}   ·   {formatter.FormatDate(clock.GetLocalNow().DateTime)}";
    }
}
