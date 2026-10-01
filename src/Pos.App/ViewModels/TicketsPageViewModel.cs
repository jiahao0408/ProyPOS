using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.Invoicing;
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
    TimeProvider clock) : PageViewModel(localizer)
{
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
        ClearMessage();
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
                L[s.Type == InvoiceType.Simplified ? "InvoiceSimplifiedShort" : "InvoiceCompleteShort"],
                status));
        }
        Selected = null;
    }
}
