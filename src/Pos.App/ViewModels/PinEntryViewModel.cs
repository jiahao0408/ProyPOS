using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Security;

namespace Pos.App.ViewModels;

/// <summary>Teclado numérico de PIN, táctil o con el teclado físico. Al llegar a 4 dígitos se envía solo.</summary>
public partial class PinEntryViewModel : ObservableObject
{
    private const int PinLength = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Masked))]
    private string _pin = "";

    /// <summary>Se dispara con el PIN completo.</summary>
    public event Action<string>? Completed;

    /// <summary>Puntos llenos por cada dígito tecleado y huecos para los que faltan.</summary>
    public string Masked => new string('●', Pin.Length) + new string('○', PinLength - Pin.Length);

    [RelayCommand]
    public void Digit(string digit)
    {
        if (Pin.Length >= PinLength || digit.Length != 1 || !char.IsAsciiDigit(digit[0]))
            return;

        Pin += digit;
        if (Pin.Length == PinLength && PinHasher.IsValidPin(Pin))
        {
            var pin = Pin;
            Pin = "";
            Completed?.Invoke(pin);
        }
    }

    [RelayCommand]
    public void Backspace()
    {
        if (Pin.Length > 0)
            Pin = Pin[..^1];
    }

    [RelayCommand]
    public void Clear() => Pin = "";
}
