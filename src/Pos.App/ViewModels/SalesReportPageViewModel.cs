using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.CashRegister;

namespace Pos.App.ViewModels;

public sealed record ReportRowView(string Name, string Count, string Total);

/// <summary>CAJ-03: ventas del periodo por producto, método de pago y cajero (las devoluciones restan).</summary>
public partial class SalesReportPageViewModel(
    ILocalizer localizer,
    SalesReportService reports,
    RegionFormatter formatter,
    TimeProvider clock) : PageViewModel(localizer)
{
    private bool _loading;

    [ObservableProperty]
    private DateTime? _from;

    [ObservableProperty]
    private DateTime? _to;

    [ObservableProperty]
    private string _ticketsText = "";

    [ObservableProperty]
    private string _salesText = "";

    [ObservableProperty]
    private string _returnsText = "";

    [ObservableProperty]
    private string _netText = "";

    [ObservableProperty]
    private string _averageText = "";

    public ObservableCollection<ReportRowView> ByProduct { get; } = [];

    public ObservableCollection<ReportRowView> ByPayment { get; } = [];

    public ObservableCollection<ReportRowView> ByCashier { get; } = [];

    public override void Load() => Today();

    partial void OnFromChanged(DateTime? value) => Refresh();

    partial void OnToChanged(DateTime? value) => Refresh();

    [RelayCommand]
    private void Today() => SetRange(clock.GetLocalNow().Date, clock.GetLocalNow().Date);

    [RelayCommand]
    private void Yesterday() => SetRange(clock.GetLocalNow().Date.AddDays(-1), clock.GetLocalNow().Date.AddDays(-1));

    [RelayCommand]
    private void ThisMonth()
    {
        var today = clock.GetLocalNow().Date;
        SetRange(new DateTime(today.Year, today.Month, 1), today);
    }

    private void SetRange(DateTime from, DateTime to)
    {
        _loading = true;
        From = from;
        To = to;
        _loading = false;
        Refresh();
    }

    private void Refresh()
    {
        if (_loading || From is not { } from || To is not { } to)
            return;

        var report = reports.Build(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));
        TicketsText = report.TicketCount.ToString(L.Culture);
        SalesText = formatter.FormatMoney(report.SalesTotal);
        ReturnsText = string.Format(L["ReportReturnsValue"], formatter.FormatMoney(report.ReturnsTotal), report.ReturnCount);
        NetText = formatter.FormatMoney(report.NetTotal);
        AverageText = formatter.FormatMoney(report.AverageTicket);

        Fill(ByProduct, report.ByProduct, l => l.Name, "ReportUnits");
        Fill(ByPayment, report.ByPayment, l => L[l.Name], "ReportTickets");
        Fill(ByCashier, report.ByCashier, l => l.Name, "ReportTickets");
    }

    private void Fill(ObservableCollection<ReportRowView> target, IEnumerable<ReportLine> lines, Func<ReportLine, string> name, string countKey)
    {
        target.Clear();
        foreach (var line in lines)
            target.Add(new ReportRowView(name(line), string.Format(L[countKey], line.Count), formatter.FormatMoney(line.Total)));
    }
}
