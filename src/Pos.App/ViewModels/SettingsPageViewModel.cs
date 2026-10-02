using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Modules;
using Pos.Core.Security;
using Pos.Data;
using Pos.Localization;

namespace Pos.App.ViewModels;

public sealed record DateFormatOption(string Format, string Title);

/// <summary>v1.1.5: un atajo editable en Ajustes.</summary>
public sealed partial class ShortcutRowViewModel(ShortcutAction action, string title, string keys) : ObservableObject
{
    public ShortcutAction Action { get; } = action;

    public string Title { get; } = title;

    [ObservableProperty]
    private string _keys = keys;
}

/// <summary>CFG-01 (idioma de la interfaz), idioma de impresión de los tickets, CFG-04 (moneda y formatos), más la inactividad (USR-01).</summary>
public partial class SettingsPageViewModel(
    ILocalizer localizer,
    SettingsStore settings,
    RegionFormatter formatter,
    PrintLocalization print,
    ISession session,
    UserLanguage userLanguage,
    ShortcutSettings shortcuts,
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

    /// <summary>v1.1.5: modo de la pantalla de venta (táctil o teclado).</summary>
    [ObservableProperty]
    private Choice<string>? _saleMode;

    [ObservableProperty]
    private IReadOnlyList<Choice<string>> _saleModes = [];

    public System.Collections.ObjectModel.ObservableCollection<ShortcutRowViewModel> ShortcutRows { get; } = [];

    /// <summary>Vuelve a los atajos de fábrica (no guarda lo demás).</summary>
    [RelayCommand]
    private void ResetShortcuts()
    {
        shortcuts.ResetToDefaults();
        LoadShortcuts();
        ShowInfo("ShortcutsResetDone");
    }

    private void LoadShortcuts()
    {
        ShortcutRows.Clear();
        foreach (var action in Enum.GetValues<ShortcutAction>())
            ShortcutRows.Add(new ShortcutRowViewModel(action, L[$"Shortcut{action}"], shortcuts.GetText(action)));
    }

    [ObservableProperty]
    private DateFormatOption? _selectedDateFormat;

    [ObservableProperty]
    private string _inactivityMinutesText = "";

    /// <summary>VEN-05: descuento máximo (%) que puede aplicar un cajero sin PIN de admin.</summary>
    [ObservableProperty]
    private string _maxCashierDiscountText = "";

    [ObservableProperty]
    private string _preview = "";

    /// <summary>CFG-06: de dónde se descarga update.json.</summary>
    [ObservableProperty]
    private string _updateFeedUrl = "";

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
        UpdateFeedUrl = settings.Get(SettingKeys.UpdateFeedUrl) is { Length: > 0 } feed ? feed : Pos.App.Updates.UpdateService.DefaultFeedUrl;
        RefreshTexts(formatter.DateFormat);
        SaleMode = SaleModes.FirstOrDefault(m => m.Value == (settings.Get(SettingKeys.SaleMode) == "Keyboard" ? "Keyboard" : "Touch"));
        LoadShortcuts();
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
            // CFG-01: cambia todos los textos sin reiniciar (salvo si este usuario tiene su propio idioma, CFG-02).
            userLanguage.Apply(session.CurrentUser);
        }

        if (!Uri.TryCreate(UpdateFeedUrl.Trim(), UriKind.Absolute, out var feedUri) || feedUri.Scheme != Uri.UriSchemeHttps)
        {
            ShowError("ErrorUpdateFeedUrl");
            return;
        }
        settings.Set(SettingKeys.UpdateFeedUrl, feedUri.ToString());

        // v1.1.5: atajos (se comprueban antes de guardar nada más) y modo de la venta.
        if (!Check(shortcuts.Save(ShortcutRows.ToDictionary(r => r.Action, r => r.Keys))))
            return;
        settings.Set(SettingKeys.SaleMode, SaleMode?.Value ?? "Touch");

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
        var mode = SaleMode?.Value;
        SaleModes = [new("Touch", L["ModeTouchLong"]), new("Keyboard", L["ModeKeyboardLong"])];
        SaleMode = SaleModes.FirstOrDefault(m => m.Value == mode) ?? SaleMode;
        ModuleNames = modules.Select(m => $"{m.Id} · {L[m.NameKey]}").ToList();
        Preview = $"{formatter.FormatMoney(1234.5m)}   ·   {formatter.FormatDate(clock.GetLocalNow().DateTime)}";
    }
}
