using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Modules;
using Pos.Data;
using Pos.Localization;

namespace Pos.App.ViewModels;

public sealed record DateFormatOption(string Format, string Title);

/// <summary>CFG-01 (idioma de la interfaz) y CFG-04 (moneda y formatos), más la inactividad (USR-01).</summary>
public partial class SettingsPageViewModel(
    ILocalizer localizer,
    SettingsStore settings,
    RegionFormatter formatter,
    TimeProvider clock,
    IEnumerable<IModule> modules) : PageViewModel(localizer)
{
    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private string _currencySymbol = "";

    [ObservableProperty]
    private DateFormatOption? _selectedDateFormat;

    [ObservableProperty]
    private string _inactivityMinutesText = "";

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
        RefreshTexts(formatter.DateFormat);
    }

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(InactivityMinutesText, out var minutes) || minutes < 0)
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
        formatter.CurrencySymbol = symbol;
        formatter.DateFormat = dateFormat;

        if (SelectedLanguage is { } language)
        {
            settings.Set(SettingKeys.Language, language.Code);
            L.SetLanguage(language.Code); // CFG-01: cambia todos los textos sin reiniciar
        }

        RefreshTexts(dateFormat);
        ShowInfo("Saved");
    }

    /// <summary>Textos que no salen de un binding a L[...] y hay que rehacer al cambiar de idioma.</summary>
    private void RefreshTexts(string dateFormat)
    {
        DateFormats = RegionFormatter.DateFormats
            .Select(f => new DateFormatOption(f, f.Length == 0 ? L["DateFormatLanguage"] : f))
            .ToList();
        SelectedDateFormat = DateFormats.FirstOrDefault(f => f.Format == dateFormat) ?? DateFormats[0];
        ModuleNames = modules.Select(m => $"{m.Id} · {L[m.NameKey]}").ToList();
        Preview = $"{formatter.FormatMoney(1234.5m)}   ·   {formatter.FormatDate(clock.GetLocalNow().DateTime)}";
    }
}
