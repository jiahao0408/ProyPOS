using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

/// <summary>Una línea de la venta original: cuántas unidades se devuelven.</summary>
public sealed partial class ReturnLineViewModel(ReturnableLine line, string unitRefund, Action changed) : ObservableObject
{
    [ObservableProperty]
    private string _quantityText = "0";

    public ReturnableLine Line { get; } = line;

    public string Description => Line.Line.Description;

    public string Sold => $"{Line.Line.Quantity}";

    public string Returnable => $"{Line.Returnable}";

    public string UnitRefund { get; } = unitRefund;

    public int Quantity => int.TryParse(QuantityText, out var q) ? q : -1;

    partial void OnQuantityTextChanged(string value) => changed();

    [RelayCommand]
    private void All() => QuantityText = Line.Returnable.ToString();
}

/// <summary>VEN-06: devolución de una venta cerrada, con motivo obligatorio.</summary>
public sealed partial class ReturnDialogViewModel : ViewModelBase
{
    private readonly RegionFormatter _formatter;
    private readonly Func<IReadOnlyList<ReturnLineRequest>, string, PaymentMethod, OperationResult> _confirm;
    private readonly Action _cancel;

    [ObservableProperty]
    private string _reason = "";

    [ObservableProperty]
    private bool _refundByCard;

    [ObservableProperty]
    private string _refundText = "";

    public ReturnDialogViewModel(ILocalizer localizer, RegionFormatter formatter, string invoiceCode, IReadOnlyList<ReturnableLine> lines,
        Func<IReadOnlyList<ReturnLineRequest>, string, PaymentMethod, OperationResult> confirm, Action cancel)
        : base(localizer)
    {
        _formatter = formatter;
        _confirm = confirm;
        _cancel = cancel;
        Title = string.Format(L["ReturnTitle"], invoiceCode);
        Lines = new ObservableCollection<ReturnLineViewModel>(lines.Where(l => l.Returnable > 0)
            .Select(l => new ReturnLineViewModel(l, formatter.FormatMoney(l.UnitRefund), Recalculate)));
        Recalculate();
    }

    public string Title { get; }

    public ObservableCollection<ReturnLineViewModel> Lines { get; }

    public IReadOnlyList<ReturnLineRequest> Requests =>
        Lines.Where(l => l.Quantity > 0).Select(l => new ReturnLineRequest(l.Line.Line.Id, l.Quantity)).ToList();

    [RelayCommand]
    private void Confirm()
    {
        if (Lines.Any(l => l.Quantity < 0))
        {
            ShowError("ErrorNumberFormat");
            return;
        }
        Check(_confirm(Requests, Reason, RefundByCard ? PaymentMethod.Card : PaymentMethod.Cash));
    }

    [RelayCommand]
    private void Cancel() => _cancel();

    private void Recalculate() =>
        RefundText = _formatter.FormatMoney(Lines.Where(l => l.Quantity > 0)
            .Sum(l => Math.Round(l.Line.UnitRefund * Math.Min(l.Quantity, l.Line.Returnable), 2)));
}

/// <summary>Un artículo que se lleva el cliente en un cambio.</summary>
public sealed record ExchangeItemRow(TicketLine Line, string Description, int Quantity, string Total);

/// <summary>
/// BAZ-07: cambio de producto. Se marca lo que devuelve, se escanea lo que se lleva y se cobra o
/// devuelve la diferencia. Si hay que devolver dinero, un cajero necesita el PIN de un admin.
/// </summary>
public sealed partial class ExchangeDialogViewModel : ViewModelBase
{
    private readonly RegionFormatter _formatter;
    private readonly CatalogService _catalog;
    private readonly UserService _users;
    private readonly bool _isAdmin;
    private readonly Func<IReadOnlyList<ReturnLineRequest>, Ticket, PaymentRequest, string?, OperationResult> _confirm;
    private readonly Action _cancel;
    private readonly Ticket _newTicket = new();
    private string? _authorizedBy;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _creditText = "";

    [ObservableProperty]
    private string _newTotalText = "";

    [ObservableProperty]
    private string _differenceText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPayment))]
    private decimal _difference;

    [ObservableProperty]
    private string _cashTenderedText = "";

    [ObservableProperty]
    private bool _payByCard;

    [ObservableProperty]
    private bool _needsAdminPin;

    public ExchangeDialogViewModel(ILocalizer localizer, RegionFormatter formatter, CatalogService catalog, UserService users,
        bool isAdmin, string invoiceCode, IReadOnlyList<ReturnableLine> lines,
        Func<IReadOnlyList<ReturnLineRequest>, Ticket, PaymentRequest, string?, OperationResult> confirm, Action cancel)
        : base(localizer)
    {
        _formatter = formatter;
        _catalog = catalog;
        _users = users;
        _isAdmin = isAdmin;
        _confirm = confirm;
        _cancel = cancel;
        Title = string.Format(L["ExchangeTitle"], invoiceCode);
        ReturnLines = new ObservableCollection<ReturnLineViewModel>(lines.Where(l => l.Returnable > 0)
            .Select(l => new ReturnLineViewModel(l, formatter.FormatMoney(l.UnitRefund), Recalculate)));
        PinEntry.Completed += OnAdminPin;
        Recalculate();
    }

    public string Title { get; }

    public ObservableCollection<ReturnLineViewModel> ReturnLines { get; }

    public ObservableCollection<ExchangeItemRow> NewItems { get; } = [];

    public PinEntryViewModel PinEntry { get; } = new();

    /// <summary>El cliente paga la diferencia (si es a favor de la tienda).</summary>
    public bool ShowPayment => Difference > 0;

    /// <summary>Escanear o teclear el código de lo que se lleva.</summary>
    [RelayCommand]
    private void AddItem()
    {
        var text = SearchText.Trim();
        var product = _catalog.FindByBarcode(text) ?? (_catalog.Search(text, limit: 2) is [var only] ? only : null);
        if (product is null)
        {
            ShowError("ErrorProductNotFound");
            return;
        }
        _newTicket.Add(TicketItem.FromProduct(product));
        SearchText = "";
        ClearMessage();
        Recalculate();
    }

    [RelayCommand]
    private void RemoveItem(ExchangeItemRow row)
    {
        _newTicket.Remove(row.Line);
        Recalculate();
    }

    [RelayCommand]
    private void Confirm()
    {
        var requests = ReturnLines.Where(l => l.Quantity > 0).Select(l => new ReturnLineRequest(l.Line.Line.Id, l.Quantity)).ToList();
        if (Difference < 0 && !_isAdmin && _authorizedBy is null)
        {
            NeedsAdminPin = true; // se devuelve dinero: hace falta un admin (VEN-06)
            ShowError("AdminPinTitle");
            return;
        }

        var payment = PaymentRequest.Cash(0);
        if (Difference > 0)
        {
            if (PayByCard)
                payment = PaymentRequest.Card(Difference);
            else if (CashTenderedText.Trim().Length == 0)
                payment = PaymentRequest.Cash(Difference);
            else if (_formatter.TryParseAmount(CashTenderedText, out var tendered))
                payment = PaymentRequest.Cash(tendered);
            else
            {
                ShowError("ErrorAmountFormat");
                return;
            }
        }
        Check(_confirm(requests, _newTicket, payment, _authorizedBy));
    }

    [RelayCommand]
    private void Cancel() => _cancel();

    private void OnAdminPin(string pin)
    {
        if (_users.VerifyAdminPin(pin) is not { } admin)
        {
            ShowError("AdminPinWrong");
            return;
        }
        _authorizedBy = admin.Name;
        NeedsAdminPin = false;
        Confirm();
    }

    private void Recalculate()
    {
        NewItems.Clear();
        foreach (var line in _newTicket.Lines)
            NewItems.Add(new ExchangeItemRow(line, line.Item.Description, line.Quantity, _formatter.FormatMoney(line.Total)));

        var credit = ReturnLines.Where(l => l.Quantity > 0).Sum(l => Math.Round(l.Line.UnitRefund * Math.Min(l.Quantity, l.Line.Returnable), 2));
        Difference = _newTicket.Total - credit;
        CreditText = _formatter.FormatMoney(credit);
        NewTotalText = _formatter.FormatMoney(_newTicket.Total);
        DifferenceText = Difference >= 0
            ? string.Format(L["ExchangeCustomerPays"], _formatter.FormatMoney(Difference))
            : string.Format(L["ExchangeShopRefunds"], _formatter.FormatMoney(-Difference));
    }
}
