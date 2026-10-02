using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;
using Pos.Core.Printing;
using Pos.Core.Security;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.CashRegister;
using Pos.Modules.Invoicing;
using Pos.Modules.Printing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

public sealed record CategoryButton(int? Id, string Name, IBrush? Background);

public sealed record GenericButton(Category Section, IBrush? Background);

/// <param name="HasVariants">BAZ-05: al tocarlo se elige la variante.</param>
public sealed record ProductButton(Product Product, string Name, string Price, bool HasVariants = false);

public sealed record TicketLineRow(TicketLine Line, string Description, int Quantity, string UnitPrice, string Total, string? DiscountText);

/// <summary>
/// Pantalla de venta (sección 2).
/// VEN-01: añadir por categoría, búsqueda o código de barras. VEN-02: cambiar cantidades y quitar líneas.
/// VEN-03/04: cobro. HW-04: lector en modo teclado. BAZ-02: artículo genérico. BAZ-03: alta rápida.
/// CAJ-01: con la caja cerrada solo deja abrirla. BAZ-05: variantes. BAZ-06: verificador de precios (F9).
/// HW-02: mantiene al día la pantalla de cliente. HW-03: abre el cajón al cobrar en efectivo y a mano con PIN.
/// </summary>
public partial class SalePageViewModel(
    ILocalizer localizer,
    ISession session,
    CashRegisterService cash,
    CatalogService catalog,
    SalesService sales,
    InvoiceService invoices,
    PrintService printing,
    ProfileStore profiles,
    UserService users,
    RegionFormatter formatter,
    CustomerDisplayViewModel customerDisplay,
    PrintLocalization printLocalization) : PageViewModel(localizer), IBarcodeTarget
{
    /// <summary>
    /// v1.1: código de un lector por puerto COM. Hace lo mismo que el lector en modo teclado: si está abierto
    /// el verificador de precios va ahí; si no, al ticket.
    /// </summary>
    public void OnBarcode(string code)
    {
        if (Dialog is PriceCheckViewModel check)
        {
            check.SearchText = code;
            check.SubmitCommand.Execute(null);
            return;
        }
        if (!IsCashOpen || IsDialogOpen)
            return;
        SearchText = code;
        SubmitSearch();
    }

    /// <summary>VEN-05: admin que autorizó con su PIN un descuento por encima del límite del cajero.</summary>
    private string? _discountAuthorizedBy;

    /// <summary>Última impresión lanzada (para esperar a que termine en los tests).</summary>
    public Task LastPrint { get; private set; } = Task.CompletedTask;

    /// <summary>Última apertura del cajón (HW-03), para esperarla en los tests.</summary>
    public Task LastDrawer { get; private set; } = Task.CompletedTask;

    private const int SearchLimit = 40;
    private const int MinBarcodeLength = 4;

    private readonly Ticket _ticket = new();
    private int? _selectedCategoryId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCashClosed))]
    private bool _isCashOpen;

    [ObservableProperty]
    private string _openingFloatText = "";

    [ObservableProperty]
    private string _cashInfo = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _searchText = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _vatSummary = "";

    [ObservableProperty]
    private string _ticketDiscountText = "";

    [ObservableProperty]
    private string _itemCountText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    private ViewModelBase? _dialog;

    public bool IsCashClosed => !IsCashOpen;

    public bool IsSearching => SearchText.Trim().Length > 0;

    public bool IsDialogOpen => Dialog is not null;

    public bool HasTicketLines => TicketLines.Count > 0;

    public ObservableCollection<CategoryButton> Categories { get; } = [];

    public ObservableCollection<GenericButton> GenericSections { get; } = [];

    public ObservableCollection<ProductButton> Products { get; } = [];

    public ObservableCollection<TicketLineRow> TicketLines { get; } = [];

    /// <summary>La vista devuelve el foco al buscador (donde escribe el lector de códigos).</summary>
    public event Action? FocusSearchRequested;

    public override void Load()
    {
        var session = cash.GetOpenSession();
        IsCashOpen = session is not null;
        if (session is null)
            return;

        CashInfo = string.Format(L["CashOpenSince"],
            formatter.FormatDateTime(session.OpenedAtUtc.ToLocalTime()),
            formatter.FormatMoney(session.OpeningFloat));

        var categories = catalog.GetCategories();
        Categories.Clear();
        Categories.Add(new CategoryButton(null, L["AllCategories"], null));
        foreach (var category in categories)
            Categories.Add(new CategoryButton(category.Id, category.Name, ParseBrush(category.Color)));

        GenericSections.Clear();
        foreach (var section in categories.Where(c => c.AllowsGenericSale))
            GenericSections.Add(new GenericButton(section, ParseBrush(section.Color)));

        RefreshProducts();
        RefreshTicket();
    }

    // --- Caja (CAJ-01) ---

    [RelayCommand]
    private void OpenCash()
    {
        if (!formatter.TryParseAmount(OpeningFloatText, out var amount))
        {
            ShowError("ErrorAmountFormat");
            return;
        }
        if (Check(cash.Open(session.CurrentUser!.Id, amount)))
        {
            ClearMessage();
            Load();
        }
    }

    // --- Añadir productos (VEN-01, HW-04) ---

    partial void OnSearchTextChanged(string value) => RefreshProducts();

    [RelayCommand]
    private void SelectCategory(CategoryButton category)
    {
        _selectedCategoryId = category.Id;
        SearchText = "";
        RefreshProducts();
    }

    [RelayCommand]
    private void AddProduct(ProductButton button)
    {
        SearchText = "";
        if (button.HasVariants)
            ChooseVariant(button.Product, 1);
        else
            AddToTicket(TicketItem.FromProduct(button.Product), 1);
    }

    /// <summary>BAZ-05: un producto con variantes no se vende tal cual; se elige la variante.</summary>
    private void ChooseVariant(Product parent, int quantity)
    {
        var variants = catalog.GetVariants(parent.Id);
        Dialog = new VariantPickerViewModel(L, formatter, parent, variants,
            pick: variant =>
            {
                CloseDialog();
                AddToTicket(TicketItem.FromProduct(variant), quantity);
            },
            cancel: CloseDialog);
    }

    /// <summary>Añade el producto, o pide la variante si la tiene.</summary>
    private void AddOrChooseVariant(Product product, int quantity)
    {
        if (catalog.WithVariants([product.Id]).Count > 0)
            ChooseVariant(product, quantity);
        else
            AddToTicket(TicketItem.FromProduct(product), quantity);
    }

    // --- Verificador de precios (BAZ-06) ---

    [RelayCommand]
    private void CheckPrice()
    {
        if (!IsCashOpen || IsDialogOpen)
            return;
        ClearMessage();
        Dialog = new PriceCheckViewModel(L, formatter, catalog, close: CloseDialog);
    }

    /// <summary>
    /// Enter en el buscador (lo que manda el lector al final de cada código).
    /// Admite "3*código" para añadir varias unidades de golpe.
    /// </summary>
    [RelayCommand]
    private void SubmitSearch()
    {
        var (quantity, text) = ParseQuantityPrefix(SearchText.Trim());
        if (text.Length == 0)
            return;

        // HW-04: un escaneo añade 1 unidad.
        var product = catalog.FindByBarcode(text);
        if (product is not null)
        {
            SearchText = "";
            AddOrChooseVariant(product, quantity);
            return;
        }

        if (Products.Count == 1)
        {
            var only = Products[0];
            SearchText = "";
            if (only.HasVariants)
                ChooseVariant(only.Product, quantity);
            else
                AddToTicket(TicketItem.FromProduct(only.Product), quantity);
            return;
        }

        // HW-04: código desconocido muestra aviso; BAZ-03: y permite darlo de alta.
        if (text.Length >= MinBarcodeLength && text.All(char.IsAsciiDigit))
        {
            SearchText = "";
            OpenQuickCreate(text, quantity);
        }
    }

    // --- Artículo genérico (BAZ-02) ---

    [RelayCommand]
    private void AddGeneric(GenericButton button)
    {
        Dialog = new GenericItemViewModel(L, formatter, button.Section,
            add: amount =>
            {
                AddToTicket(TicketItem.Generic(button.Section, amount), 1);
                CloseDialog();
                return OperationResult.Ok();
            },
            cancel: CloseDialog);
    }

    // --- Ticket (VEN-02) ---

    [RelayCommand]
    private void IncreaseLine(TicketLineRow row) => ChangeQuantity(row, row.Line.Quantity + 1);

    [RelayCommand]
    private void DecreaseLine(TicketLineRow row) => ChangeQuantity(row, row.Line.Quantity - 1);

    [RelayCommand]
    private void RemoveLine(TicketLineRow row) => ChangeQuantity(row, 0);

    [RelayCommand]
    private void ClearTicket()
    {
        _ticket.Clear();
        _discountAuthorizedBy = null;
        RefreshTicket();
        FocusSearchRequested?.Invoke();
    }

    // --- Cobro (VEN-03, VEN-04) ---

    [RelayCommand]
    private void Charge()
    {
        if (!IsCashOpen || IsDialogOpen)
            return;
        if (_ticket.IsEmpty)
        {
            ShowError("ErrorTicketEmpty");
            return;
        }

        ClearMessage();
        Dialog = new PaymentViewModel(L, formatter, _ticket.Total, confirm: Pay, cancel: CloseDialog,
            findCustomer: invoices.FindCustomer, printLanguage: printLocalization.CurrentLanguage);
    }

    // --- Descuentos (VEN-05) ---

    [RelayCommand]
    private void EditLineDiscount(TicketLineRow row)
    {
        var line = row.Line;
        var before = (line.DiscountPercent, line.DiscountAmount);
        Dialog = new DiscountViewModel(L, formatter, string.Format(L["LineDiscountTitle"], line.Item.Description), allowAmount: true,
            apply: (percent, amount) =>
            {
                if (amount > line.Gross)
                    return OperationResult.Fail("ErrorDiscountTooBig");
                _ticket.SetLineDiscount(line, percent, amount);
                return AfterDiscount(() => _ticket.SetLineDiscount(line, before.DiscountPercent, before.DiscountAmount));
            },
            cancel: CloseDialog);
    }

    [RelayCommand]
    private void EditTicketDiscount()
    {
        if (_ticket.IsEmpty)
            return;
        var before = _ticket.DiscountPercent;
        Dialog = new DiscountViewModel(L, formatter, L["TicketDiscountTitle"], allowAmount: false,
            apply: (percent, _) =>
            {
                _ticket.SetDiscountPercent(percent ?? 0);
                return AfterDiscount(() => _ticket.SetDiscountPercent(before));
            },
            cancel: CloseDialog);
    }

    /// <summary>Si un cajero se pasa del límite, pide el PIN de un admin; si cancela, se deshace el descuento.</summary>
    private OperationResult AfterDiscount(Action undo)
    {
        RefreshTicket();
        var isAdmin = session.CurrentUser?.IsAdmin == true;
        if (isAdmin || _discountAuthorizedBy is not null || _ticket.MaxEffectiveDiscountPercent <= sales.MaxCashierDiscount)
        {
            CloseDialog();
            return OperationResult.Ok();
        }

        Dialog = new AdminPinPromptViewModel(L, users,
            onAuthorized: admin =>
            {
                _discountAuthorizedBy = admin.Name;
                CloseDialog();
            },
            onCancel: () =>
            {
                undo();
                RefreshTicket();
                CloseDialog();
            });
        return OperationResult.Ok();
    }

    // --- Cierre de caja (CAJ-02) ---

    [RelayCommand]
    private void StartCloseCash()
    {
        if (!IsCashOpen || IsDialogOpen || cash.GetOpenSession() is not { } open || cash.GetSummary(open.Id) is not { } summary)
            return;
        if (!_ticket.IsEmpty)
        {
            ShowError("ErrorTicketNotEmpty");
            return;
        }

        Dialog = new CloseCashViewModel(L, formatter, summary,
            close: counted =>
            {
                var result = cash.Close(session.CurrentUser!.Id, counted);
                if (!result.Success)
                    return result;

                var closed = result.Value!;
                Dialog = null;
                var z = ToZReport(closed);
                LastPrint = PrintZAsync(z);
                Load(); // la pantalla vuelve a "caja cerrada"
                Message = string.Format(L["CashClosedSummary"], z.ZNumber, formatter.FormatMoney(z.Difference));
                MessageIsError = z.Difference != 0;
                return OperationResult.Ok();
            },
            cancel: CloseDialog);
    }

    private ZReportDocument ToZReport(CashSummary closed)
    {
        var s = closed.Session;
        return new ZReportDocument(s.ZNumber!.Value, s.OpenedAtUtc, s.ClosedAtUtc!.Value, session.CurrentUser?.Name ?? "",
            closed.SalesCount, closed.SalesTotal, closed.CashTotal, closed.CardTotal, s.OpeningFloat,
            closed.ExpectedCash, s.CountedCash!.Value,
            closed.VatLines.Select(v => new ZReportVatLine(v.Rate, v.Base, v.VatAmount, v.Total)).ToList());
    }

    /// <summary>CAJ-02: "imprime el cierre Z". Si falla, se avisa; el cierre ya está guardado.</summary>
    private async Task PrintZAsync(ZReportDocument z)
    {
        var outcome = await printing.PrintZReportAsync(z);
        if (!outcome.Success)
        {
            Message = string.Format(L["ErrorPrintFailed"], outcome.Error);
            MessageIsError = true;
        }
    }

    [RelayCommand]
    private void CloseDialog()
    {
        Dialog = null;
        FocusSearchRequested?.Invoke();
    }

    /// <param name="ticketLanguage">CFG-03: idioma del ticket de esta venta (solo cambian los textos, no los datos fiscales).</param>
    private OperationResult Pay(PaymentRequest payment, InvoiceCustomer? customer, string? ticketLanguage)
    {
        var result = sales.Checkout(_ticket, payment, session.CurrentUser!.Id, customer, _discountAuthorizedBy);
        if (!result.Success)
            return result;

        var (sale, change) = result.Value!;
        _ticket.Clear();
        _discountAuthorizedBy = null;
        CloseDialog();
        RefreshTicket();
        customerDisplay.ShowThanks(sale.Total, change);

        // HW-03: el cajón se abre solo si se cobra algo en efectivo.
        if (payment.CardAmount < sale.Total && profiles.GetHardware().OpenDrawerOnCash)
            LastDrawer = OpenDrawerAndReportAsync();

        var invoice = invoices.GetCurrentForSale(sale.Id);
        var completed = string.Format(L["SaleCompleted"], invoice?.Code ?? sale.Id.ToString(L.Culture), formatter.FormatMoney(change));
        Message = completed;
        MessageIsError = false;

        // IMP-01: impresión automática configurable (sí / no / preguntar).
        if (invoice is not null)
        {
            switch (profiles.GetPrinter().AutoPrint)
            {
                case AutoPrintMode.Yes:
                    Print(invoice.InvoiceId, ticketLanguage);
                    break;
                case AutoPrintMode.Ask:
                    Dialog = new PrintPromptViewModel(L, completed,
                        print: () =>
                        {
                            CloseDialog();
                            Print(invoice.InvoiceId, ticketLanguage);
                        },
                        skip: CloseDialog);
                    break;
            }
        }
        return OperationResult.Ok();
    }

    // --- Cajón (HW-03) ---

    /// <summary>Apertura manual (sin venta): un cajero necesita el PIN de un administrador; queda en la auditoría.</summary>
    [RelayCommand]
    private void OpenDrawer()
    {
        if (!IsCashOpen || IsDialogOpen)
            return;
        var user = session.CurrentUser!;
        if (user.IsAdmin)
        {
            cash.RecordDrawerOpened(user.Id, authorizedBy: null);
            LastDrawer = OpenDrawerAndReportAsync();
            return;
        }

        Dialog = new AdminPinPromptViewModel(L, users,
            onAuthorized: admin =>
            {
                CloseDialog();
                cash.RecordDrawerOpened(user.Id, admin.Name);
                LastDrawer = OpenDrawerAndReportAsync();
            },
            onCancel: CloseDialog);
    }

    private async Task OpenDrawerAndReportAsync()
    {
        var outcome = await printing.OpenDrawerAsync();
        if (!outcome.Success)
        {
            Message = string.Format(L["ErrorDrawerFailed"], outcome.Error);
            MessageIsError = true;
        }
    }

    /// <summary>Imprime sin bloquear la caja; si falla, la venta ya está guardada y solo se avisa.</summary>
    private void Print(int invoiceId, string? language)
    {
        LastPrint = PrintAndReportAsync(invoiceId, language);
    }

    private async Task PrintAndReportAsync(int invoiceId, string? language)
    {
        var outcome = await printing.PrintInvoiceAsync(invoiceId, copy: false, language);
        if (!outcome.Success)
        {
            Message = string.Format(L["ErrorPrintFailed"], outcome.Error);
            MessageIsError = true;
        }
    }

    // --- Alta rápida (BAZ-03) ---

    private void OpenQuickCreate(string barcode, int quantity)
    {
        var isAdmin = session.CurrentUser?.IsAdmin == true;
        Dialog = new QuickCreateViewModel(L, formatter, barcode, catalog.GetCategories(), createdByCashier: !isAdmin,
            save: (name, price, sectionId) =>
            {
                var created = catalog.QuickCreate(name, price, sectionId, barcode, createdByAdmin: isAdmin);
                if (!created.Success)
                    return created;
                AddToTicket(TicketItem.FromProduct(created.Value!), quantity);
                CloseDialog();
                RefreshProducts();
                return OperationResult.Ok();
            },
            cancel: CloseDialog);
    }

    // --- Ayudantes ---

    private void AddToTicket(TicketItem item, int quantity)
    {
        _ticket.Add(item, quantity);
        ClearMessage();
        RefreshTicket();
        FocusSearchRequested?.Invoke();
    }

    private void ChangeQuantity(TicketLineRow row, int quantity)
    {
        _ticket.SetQuantity(row.Line, quantity);
        RefreshTicket();
    }

    private void RefreshTicket()
    {
        customerDisplay.ShowTicket(_ticket);
        TicketLines.Clear();
        foreach (var line in _ticket.Lines)
        {
            TicketLines.Add(new TicketLineRow(line, line.Item.Description, line.Quantity,
                formatter.FormatMoney(line.Item.UnitPrice), formatter.FormatMoney(line.Total),
                line.Discount > 0 ? string.Format(L["DiscountApplied"], formatter.FormatMoney(line.Discount)) : null));
        }

        TotalText = formatter.FormatMoney(_ticket.Total);
        TicketDiscountText = _ticket.DiscountPercent > 0
            ? string.Format(L["TicketDiscount"], _ticket.DiscountPercent.ToString("0.##", L.Culture))
            : "";
        ItemCountText = string.Format(L["TicketItemCount"], _ticket.ItemCount);
        VatSummary = string.Join("   ", _ticket.VatBreakdown().Select(v =>
            string.Format(L["VatLine"], v.Rate.ToString("0.##", L.Culture), formatter.FormatMoney(v.Base), formatter.FormatMoney(v.VatAmount))));
        OnPropertyChanged(nameof(HasTicketLines));
    }

    private void RefreshProducts()
    {
        if (!IsCashOpen)
            return;

        // Al navegar por categorías salen los productos (las variantes se eligen al tocarlos);
        // al buscar también salen las variantes, para encontrar "camiseta roja" directamente.
        var products = IsSearching
            ? catalog.Search(ParseQuantityPrefix(SearchText.Trim()).Text, limit: SearchLimit)
            : catalog.Search(null, _selectedCategoryId, includeVariants: false);
        var withVariants = catalog.WithVariants(products.Select(p => p.Id));

        Products.Clear();
        foreach (var p in products)
            Products.Add(new ProductButton(p, p.Name, formatter.FormatMoney(p.Price), withVariants.Contains(p.Id)));
    }

    private static (int Quantity, string Text) ParseQuantityPrefix(string text)
    {
        var star = text.IndexOf('*');
        if (star > 0 && int.TryParse(text[..star], out var quantity) && quantity is > 0 and <= 999)
            return (quantity, text[(star + 1)..].Trim());
        return (1, text);
    }

    private static IBrush? ParseBrush(string? color) =>
        color is not null && Color.TryParse(color, out var parsed) ? new SolidColorBrush(parsed) : null;
}
