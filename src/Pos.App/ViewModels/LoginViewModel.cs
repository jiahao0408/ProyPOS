using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

/// <summary>USR-01: el usuario toca su nombre y teclea su PIN de 4 dígitos.</summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly UserService _users;
    private readonly ISession _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingUser))]
    private User? _selectedUser;

    public LoginViewModel(ILocalizer localizer, UserService users, ISession session)
        : base(localizer)
    {
        _users = users;
        _session = session;
        Users = new ObservableCollection<User>(users.GetUsers());
        PinEntry.Completed += SubmitPin;
    }

    public ObservableCollection<User> Users { get; }

    public PinEntryViewModel PinEntry { get; } = new();

    public bool IsChoosingUser => SelectedUser is null;

    [RelayCommand]
    private void SelectUser(User user)
    {
        SelectedUser = user;
        PinEntry.Clear();
        ClearMessage();
    }

    [RelayCommand]
    private void BackToUsers()
    {
        SelectedUser = null;
        PinEntry.Clear();
        ClearMessage();
    }

    private void SubmitPin(string pin)
    {
        if (SelectedUser is null)
            return;

        var result = _users.SignIn(SelectedUser.Id, pin);
        switch (result.Status)
        {
            case LoginStatus.Success:
                _session.SignIn(result.User!);
                break;
            case LoginStatus.WrongPin:
                Message = string.Format(L["WrongPinRemaining"], result.RemainingAttempts);
                MessageIsError = true;
                break;
            case LoginStatus.Locked:
                var until = result.LockedUntilUtc!.Value.ToLocalTime();
                Message = string.Format(L["UserLocked"], until.ToString("t", L.Culture));
                MessageIsError = true;
                break;
            default:
                ShowError("ErrorUserNotFound");
                break;
        }
    }
}
