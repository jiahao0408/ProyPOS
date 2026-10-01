using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.Sales;

namespace Pos.App.ViewModels;

public enum PaymentMode
{
    Cash,
    Card,
    Mixed,
}

public sealed record QuickCashOption(decimal Amount, string Title);

/// <summary>Ventana de cobro: efectivo con cambio (VEN-03), tarjeta o mixto (VEN-04).</summary>
public sealed partial class PaymentViewModel : ViewModelBase
{
    private readonly RegionFormatter _formatter;
    private readonly Func<PaymentRequest, OperationResult> _confirm;
    private readonly Action _cancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCash), nameof(IsCard), nameof(IsMixed), nameof(ShowCashInput), nameof(ShowCardInput))]
    private PaymentMode _mode = PaymentMode.Cash;

    [ObservableProperty]
    private string _cashTenderedText = "";

    [ObservableProperty]
    private string _cardAmountText = "";

    [ObservableProperty]
    private string _cashDueText = "";

    [ObservableProperty]
    private string _changeText = "";

    public PaymentViewModel(ILocalizer localizer, RegionFormatter formatter, decimal total,
        Func<PaymentRequest, OperationResult> confirm, Action cancel)
        : base(localizer)
    {
        _formatter = formatter;
        _confirm = confirm;
        _cancel = cancel;
        Total = total;
        TotalText = formatter.FormatMoney(total);

        // Importe exacto y billetes habituales que cubren el total.
        QuickCash = new[] { 5m, 10m, 20m, 50m, 100m }
            .Where(b => b >= total)
            .Take(4)
            .Prepend(total)
            .Distinct()
            .Select(a => new QuickCashOption(a, formatter.FormatMoney(a)))
            .ToList();
        Recalculate();
    }

    public decimal Total { get; }

    public string TotalText { get; }

    public IReadOnlyList<QuickCashOption> QuickCash { get; }

    public bool IsCash => Mode == PaymentMode.Cash;

    public bool IsCard => Mode == PaymentMode.Card;

    public bool IsMixed => Mode == PaymentMode.Mixed;

    public bool ShowCashInput => Mode != PaymentMode.Card;

    public bool ShowCardInput => Mode == PaymentMode.Mixed;

    partial void OnModeChanged(PaymentMode value) => Recalculate();

    partial void OnCashTenderedTextChanged(string value) => Recalculate();

    partial void OnCardAmountTextChanged(string value) => Recalculate();

    [RelayCommand]
    private void SetMode(PaymentMode mode)
    {
        Mode = mode;
        ClearMessage();
    }

    [RelayCommand]
    private void UseQuickCash(QuickCashOption option) => CashTenderedText = _formatter.FormatAmount(option.Amount);

    [RelayCommand]
    private void Cancel() => _cancel();

    [RelayCommand]
    private void Confirm()
    {
        if (!TryBuildRequest(out var request))
        {
            ShowError("ErrorAmountFormat");
            return;
        }
        Check(_confirm(request));
    }

    /// <summary>
    /// En efectivo, dejar el importe vacío significa "me da justo". En mixto, la tarjeta
    /// es obligatoria y el efectivo vacío es el resto justo.
    /// </summary>
    private bool TryBuildRequest(out PaymentRequest request)
    {
        request = PaymentRequest.Card(Total);
        if (Mode == PaymentMode.Card)
            return true;

        var card = 0m;
        if (Mode == PaymentMode.Mixed && !_formatter.TryParseAmount(CardAmountText, out card))
            return false;

        var due = Math.Max(0m, Total - card);
        var tendered = due;
        if (!string.IsNullOrWhiteSpace(CashTenderedText) && !_formatter.TryParseAmount(CashTenderedText, out tendered))
            return false;

        request = new PaymentRequest(card, tendered);
        return true;
    }

    private void Recalculate()
    {
        if (!TryBuildRequest(out var request))
        {
            CashDueText = ChangeText = "";
            return;
        }
        var (cashDue, change) = SalesService.Preview(Total, request);
        CashDueText = _formatter.FormatMoney(cashDue);
        ChangeText = _formatter.FormatMoney(change);
    }
}

/// <summary>BAZ-03: alta rápida de un código escaneado que no existe.</summary>
public sealed partial class QuickCreateViewModel : ViewModelBase
{
    private readonly RegionFormatter _formatter;
    private readonly Func<string, decimal, int?, OperationResult> _save;
    private readonly Action _cancel;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _priceText = "";

    [ObservableProperty]
    private CategoryOption? _selectedSection;

    public QuickCreateViewModel(ILocalizer localizer, RegionFormatter formatter, string barcode,
        IEnumerable<Category> sections, bool createdByCashier,
        Func<string, decimal, int?, OperationResult> save, Action cancel)
        : base(localizer)
    {
        _formatter = formatter;
        _save = save;
        _cancel = cancel;
        Barcode = barcode;
        CreatedByCashier = createdByCashier;
        Sections = new ObservableCollection<CategoryOption>(
            sections.Select(c => new CategoryOption(c.Id, c.Name)).Prepend(new CategoryOption(null, L["NoCategory"])));
        SelectedSection = Sections[0];
        UnknownBarcodeText = string.Format(L["UnknownBarcode"], barcode);
    }

    public string Barcode { get; }

    public bool CreatedByCashier { get; }

    public string UnknownBarcodeText { get; }

    public ObservableCollection<CategoryOption> Sections { get; }

    [RelayCommand]
    private void Save()
    {
        if (!_formatter.TryParseAmount(PriceText, out var price))
        {
            ShowError("ErrorAmountFormat");
            return;
        }
        Check(_save(Name, price, SelectedSection?.Id));
    }

    [RelayCommand]
    private void Cancel() => _cancel();
}

/// <summary>BAZ-02: artículo genérico de una sección con precio libre.</summary>
public sealed partial class GenericItemViewModel(
    ILocalizer localizer,
    RegionFormatter formatter,
    Category section,
    Func<decimal, OperationResult> add,
    Action cancel) : ViewModelBase(localizer)
{
    [ObservableProperty]
    private string _amountText = "";

    public string SectionName => section.Name;

    [RelayCommand]
    private void Add()
    {
        if (!formatter.TryParseAmount(AmountText, out var amount) || amount <= 0)
        {
            ShowError("ErrorAmountFormat");
            return;
        }
        Check(add(amount));
    }

    [RelayCommand]
    private void Cancel() => cancel();
}
