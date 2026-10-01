using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

/// <summary>Primer arranque: con la BD vacía se crea el administrador inicial.</summary>
public partial class FirstRunViewModel(ILocalizer localizer, UserService users, ISession session) : ViewModelBase(localizer)
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _pin = "";

    [ObservableProperty]
    private string _pinConfirm = "";

    [RelayCommand]
    private void CreateAdmin()
    {
        if (Pin != PinConfirm)
        {
            ShowError("ErrorPinMismatch");
            return;
        }

        var result = users.CreateFirstAdmin(Name, Pin);
        if (Check(result))
            session.SignIn(result.Value!);
    }
}
