using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Core.Localization;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Hardware;
using Pos.Modules.Sales;

namespace Pos.App.ViewModels;

public sealed record CustomerDisplayLine(string Description, string Quantity, string Total);

public enum CustomerDisplayState
{
    /// <summary>Sin venta en curso: mensaje de bienvenida.</summary>
    Welcome,

    /// <summary>Cada línea añadida y el total.</summary>
    Ticket,

    /// <summary>Recién cobrado: total pagado y cambio, hasta que empieza la siguiente venta.</summary>
    Thanks,
}

/// <summary>
/// HW-02: lo que ve el cliente en la segunda pantalla. La pantalla de venta le pasa el ticket; los
/// textos salen en el idioma de impresión (el del cliente), no en el del cajero.
/// Hay una sola para toda la app: la ventana del segundo monitor se abre o se cierra con <see cref="IsEnabled"/>.
/// </summary>
public sealed partial class CustomerDisplayViewModel : ObservableObject
{
    private readonly PrintLocalization _print;
    private readonly ProfileStore _profiles;
    private readonly PoleDisplay _pole;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsTicket), nameof(IsThanks))]
    private CustomerDisplayState _state;

    [ObservableProperty]
    private string _businessName = "";

    [ObservableProperty]
    private string _welcomeText = "";

    [ObservableProperty]
    private string _totalLabel = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _thanksText = "";

    [ObservableProperty]
    private string _changeText = "";

    public CustomerDisplayViewModel(PrintLocalization print, ProfileStore profiles, PoleDisplay pole)
    {
        _print = print;
        _profiles = profiles;
        _pole = pole;
        IsEnabled = profiles.GetHardware().CustomerDisplay;
        ShowWelcome();
    }

    public ObservableCollection<CustomerDisplayLine> Lines { get; } = [];

    public bool IsWelcome => State == CustomerDisplayState.Welcome;

    public bool IsTicket => State == CustomerDisplayState.Ticket;

    public bool IsThanks => State == CustomerDisplayState.Thanks;

    /// <summary>v1.1: lo último enviado al visor de 2 × 20 (para las pruebas).</summary>
    public (string Line1, string Line2) PoleLines { get; private set; }

    public Task LastPole { get; private set; } = Task.CompletedTask;

    private void Pole(string line1, string line2)
    {
        PoleLines = (line1, line2);
        LastPole = _pole.ShowAsync(line1, line2);
    }

    /// <summary>El ticket ha cambiado. Vacío: se queda en "gracias" si se acaba de cobrar, o vuelve a la bienvenida.</summary>
    public void ShowTicket(Ticket ticket)
    {
        if (ticket.IsEmpty)
        {
            if (State != CustomerDisplayState.Thanks)
                ShowWelcome();
            return;
        }

        var (L, formatter) = _print.For();
        Lines.Clear();
        foreach (var line in ticket.Lines)
            Lines.Add(new CustomerDisplayLine(line.Item.Description, $"{line.Quantity} ×", formatter.FormatMoney(line.Total)));
        TotalLabel = L["Total"];
        TotalText = formatter.FormatMoney(ticket.Total);
        State = CustomerDisplayState.Ticket;

        // Visor: el último producto añadido y el total.
        var last = ticket.Lines[^1];
        Pole(PoleDisplay.Columns2(last.Item.Description, formatter.FormatAmount(last.Total)),
            PoleDisplay.Columns2(L["Total"].ToUpperInvariant(), formatter.FormatAmount(ticket.Total)));
    }

    public void ShowThanks(decimal total, decimal change)
    {
        var (L, formatter) = _print.For();
        Lines.Clear();
        TotalLabel = L["Total"];
        TotalText = formatter.FormatMoney(total);
        ThanksText = L["CustomerDisplayThanks"];
        ChangeText = change > 0 ? string.Format(L["CustomerDisplayChange"], formatter.FormatMoney(change)) : "";
        State = CustomerDisplayState.Thanks;

        Pole(PoleDisplay.Columns2(L["Total"].ToUpperInvariant(), formatter.FormatAmount(total)),
            change > 0 ? PoleDisplay.Columns2(L["Change"], formatter.FormatAmount(change)) : PoleDisplay.Center(L["CustomerDisplayThanks"]));
    }

    public void ShowWelcome()
    {
        var (L, _) = _print.For();
        var hardware = _profiles.GetHardware();
        BusinessName = _profiles.GetBusiness().Name;
        WelcomeText = hardware.WelcomeMessage.Length > 0 ? hardware.WelcomeMessage : L["CustomerDisplayWelcome"];
        Lines.Clear();
        State = CustomerDisplayState.Welcome;
        Pole(PoleDisplay.Center(BusinessName), PoleDisplay.Center(WelcomeText));
    }
}
