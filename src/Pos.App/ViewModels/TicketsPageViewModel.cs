using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.Invoicing;
using Pos.Modules.Users;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Data;
using Pos.Core.Security;
using Pos.Modules.Printing;

namespace Pos.App.ViewModels;

public sealed record InvoiceRow(InvoiceSummary Summary, string Code, string Time, string Total, string Kind, string? Status);

/// <summary>Ventana para pedir los datos del cliente al facturar un ticket (FAC-06).</summary>
public sealed partial class InvoiceTicketViewModel : ViewModelBase
{
    private readonly Func<InvoiceCustomer, OperationResult> _issue;
    private readonly Action _cancel;

    public InvoiceTicketViewModel(ILocalizer localizer, string ticketCode, Func<string, Customer?> findCustomer,
        Func<InvoiceCustomer, OperationResult> issue, Action cancel)
        : base(localizer)
    {
        _issue = issue;
        _cancel = cancel;
        Title = string.Format(L["InvoiceTicketTitle"], ticketCode);
        Customer = new CustomerFormViewModel(localizer, findCustomer);
    }

    public string Title { get; }

    public CustomerFormViewModel Customer { get; }

    [RelayCommand]
    private void Issue() => Check(_issue(Customer.ToCustomer()));

    [RelayCommand]
    private void Cancel() => _cancel();
}

/// <summary>
/// Tickets emitidos: buscar por número, fecha o escaneando el QR del ticket,
/// reimprimir con la marca COPIA (IMP-02), facturar un ticket (FAC-06) y guardar el PDF.
/// </summary>
public partial class TicketsPageViewModel(
    ILocalizer localizer,
    InvoiceService invoices,
    PrintService printing,
    InvoicePdf pdf,
    RegionFormatter formatter,
    TimeProvider clock,
    SalesService sales,
    CatalogService catalog,
    UserService users,
    ISession session,
    ProfileStore profiles) : PageViewModel(localizer)
{
    [ObservableProperty]
    private bool _canReturn;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private DateTime? _day;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanInvoice))]
    private InvoiceRow? _selected;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    private ViewModelBase? _dialog;

    public ObservableCollection<InvoiceRow> Invoices { get; } = [];

    public bool HasSelection => Selected is not null;

    /// <summary>FAC-06: solo un ticket (simplificada) que aún no se haya facturado.</summary>
    public bool CanInvoice => Selected is { Summary: { Type: InvoiceType.Simplified, IsReplaced: false } };

    public bool IsDialogOpen => Dialog is not null;

    /// <summary>Última impresión lanzada (para esperarla en los tests).</summary>
    public Task LastPrint { get; private set; } = Task.CompletedTask;

    public override void Load() => Day = clock.GetLocalNow().Date;

    partial void OnDayChanged(DateTime? value)
    {
        if (value is { } day)
            Show(invoices.ListByDay(DateOnly.FromDateTime(day)));
    }

    partial void OnSelectedChanged(InvoiceRow? value)
    {
        Preview = value is null ? "" : invoices.Get(value.Summary.Id) is { } doc ? printing.Preview(doc, copy: false) : "";
        // VEN-06 / BAZ-07: se puede devolver o cambiar si es una venta (no una rectificativa) con unidades pendientes.
        CanReturn = value is { Summary.Type: not InvoiceType.Rectificative }
                    && sales.GetReturnable(value.Summary.SaleId).Any(l => l.Returnable > 0);
        ClearMessage();
    }

    // --- Devoluciones (VEN-06, FAC-03) ---

    /// <summary>Exige PIN de admin (si quien está es un cajero) y motivo.</summary>
    [RelayCommand]
    private void Return()
    {
        if (Selected is not { } row || !CanReturn)
            return;

        void Open(string? authorizedBy) =>
            Dialog = new ReturnDialogViewModel(L, formatter, row.Code, sales.GetReturnable(row.Summary.SaleId),
                confirm: (requests, reason, method) =>
                {
                    var result = sales.Return(row.Summary.SaleId, requests, reason, method, session.CurrentUser!.Id, authorizedBy);
                    if (!result.Success)
                        return result;
                    Dialog = null;
                    AfterReturn(result.Value!, string.Format(L["ReturnDone"], "{0}", formatter.FormatMoney(-result.Value!.Total)));
                    return Core.OperationResult.Ok();
                },
                cancel: () => Dialog = null);

        if (session.CurrentUser?.IsAdmin == true)
            Open(null);
        else
            Dialog = new AdminPinPromptViewModel(L, users, admin => Open(admin.Name), () => Dialog = null);
    }

    // --- Cambios y ticket regalo (BAZ-07) ---

    [RelayCommand]
    private void Exchange()
    {
        if (Selected is not { } row || !CanReturn)
            return;

        Dialog = new ExchangeDialogViewModel(L, formatter, catalog, users, session.CurrentUser?.IsAdmin == true, row.Code,
            sales.GetReturnable(row.Summary.SaleId),
            confirm: (requests, newTicket, payment, authorizedBy) =>
            {
                var result = sales.Exchange(row.Summary.SaleId, requests, newTicket, payment, PaymentMethod.Cash,
                    session.CurrentUser!.Id, L["ExchangeReason"], authorizedBy);
                if (!result.Success)
                    return result;
                Dialog = null;
                var (returned, newSale, difference, change) = result.Value!;
                AfterReturn(returned, string.Format(L["ExchangeDone"], "{0}", formatter.FormatMoney(difference), formatter.FormatMoney(change)), newSale);
                return Core.OperationResult.Ok();
            },
            cancel: () => Dialog = null);
    }

    [RelayCommand]
    private void GiftTicket()
    {
        if (Selected is not { } row)
            return;
        LastPrint = PrintGiftAsync(row.Summary.Id);
    }

    private async Task PrintGiftAsync(int invoiceId)
    {
        var outcome = await printing.PrintGiftAsync(invoiceId);
        Message = outcome.Success ? L["GiftTicketPrinted"] : string.Format(L["ErrorPrintFailed"], outcome.Error);
        MessageIsError = !outcome.Success;
    }

    /// <summary>Muestra la rectificativa (y la venta nueva de un cambio) y la imprime si la impresión automática está activada.</summary>
    private void AfterReturn(Sale returned, string messageTemplate, Sale? newSale = null)
    {
        var rectificative = invoices.GetCurrentForSale(returned.Id)!;
        var newInvoice = newSale is null ? null : invoices.GetCurrentForSale(newSale.Id);
        OnDayChanged(Day);
        Selected = Invoices.FirstOrDefault(i => i.Summary.Id == rectificative.InvoiceId);
        Message = string.Format(messageTemplate, rectificative.Code);
        MessageIsError = false;

        if (profiles.GetPrinter().AutoPrint != AutoPrintMode.No)
            LastPrint = PrintSequenceAsync(rectificative.InvoiceId, newInvoice?.InvoiceId);
    }

    private async Task PrintSequenceAsync(int first, int? second)
    {
        await PrintAsync(first, copy: false);
        if (second is { } id)
            await PrintAsync(id, copy: false);
    }

    /// <summary>Enter en el buscador; acepta el número, el código completo o lo que lea el lector del QR.</summary>
    [RelayCommand]
    private void Search()
    {
        if (SearchText.Trim().Length == 0)
        {
            OnDayChanged(Day);
            return;
        }
        Show(invoices.Search(SearchText));
        if (Invoices.Count == 1)
            Selected = Invoices[0];
    }

    [RelayCommand]
    private void Reprint()
    {
        if (Selected is { } row)
            LastPrint = PrintAsync(row.Summary.Id, copy: true);
    }

    [RelayCommand]
    private void InvoiceTicket()
    {
        if (Selected is not { } row || !CanInvoice)
            return;

        Dialog = new InvoiceTicketViewModel(L, row.Code, invoices.FindCustomer,
            issue: customer =>
            {
                var result = invoices.IssueFromTicket(row.Summary.Id, customer);
                if (!result.Success)
                    return result;

                Dialog = null;
                var code = result.Value!.Code;
                Show(invoices.Search(code).Concat(invoices.Search(row.Code)).ToList());
                Selected = Invoices.FirstOrDefault(r => r.Code == code);
                LastPrint = PrintAsync(result.Value.InvoiceId, copy: false);
                Message = string.Format(L["InvoiceIssued"], code);
                MessageIsError = false;
                return OperationResult.Ok();
            },
            cancel: () => Dialog = null);
    }

    /// <summary>Lo llama la vista con la ruta elegida en el diálogo "Guardar como".</summary>
    public void SavePdf(string path)
    {
        if (Selected is null || invoices.Get(Selected.Summary.Id) is not { } doc)
            return;
        pdf.Save(doc, path);
        Message = string.Format(L["PdfSaved"], Path.GetFileName(path));
        MessageIsError = false;
    }

    public string SuggestedPdfName => $"{Selected?.Code ?? "factura"}.pdf";

    private async Task PrintAsync(int invoiceId, bool copy)
    {
        var outcome = await printing.PrintInvoiceAsync(invoiceId, copy);
        if (outcome.Success)
            ShowInfo("Printed");
        else
        {
            Message = string.Format(L["ErrorPrintFailed"], outcome.Error);
            MessageIsError = true;
        }
    }

    private void Show(IEnumerable<InvoiceSummary> summaries)
    {
        Invoices.Clear();
        foreach (var s in summaries)
        {
            var status = s.IsReplaced ? string.Format(L["InvoiceReplacedBy"], s.ReplacedByCode) : s.CustomerName;
            Invoices.Add(new InvoiceRow(s, s.Code,
                formatter.FormatDateTime(s.IssuedAtUtc.ToLocalTime()),
                formatter.FormatMoney(s.Total),
                L[s.Type switch
                {
                    InvoiceType.Simplified => "InvoiceSimplifiedShort",
                    InvoiceType.Complete => "InvoiceCompleteShort",
                    _ => "InvoiceRectificativeShort",
                }],
                status));
        }
        Selected = null;
    }
}
