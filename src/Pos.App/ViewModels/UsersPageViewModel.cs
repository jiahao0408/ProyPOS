using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

public sealed record UserRow(int Id, string Name, string Role, bool IsActive, bool IsLocked);

public sealed record RoleOption(Role Role, string Title);

/// <summary>USR-02: usuarios con rol admin o cajero.</summary>
public partial class UsersPageViewModel(ILocalizer localizer, UserService users, TimeProvider clock) : PageViewModel(localizer)
{
    private int? _editingId;

    [ObservableProperty]
    private UserRow? _selectedRow;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private RoleOption? _selectedRole;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string _pin = "";

    [ObservableProperty]
    private bool _isEditingExisting;

    [ObservableProperty]
    private bool _isLocked;

    public ObservableCollection<UserRow> Users { get; } = [];

    public IReadOnlyList<RoleOption> RoleOptions =>
    [
        new RoleOption(Role.Cashier, L["RoleCashier"]),
        new RoleOption(Role.Admin, L["RoleAdmin"]),
    ];

    /// <summary>Al editar, el PIN es opcional (vacío = no cambiarlo).</summary>
    public string PinLabel => L[IsEditingExisting ? "NewPinOptional" : "Pin"];

    public override void Load()
    {
        Refresh();
        New();
    }

    partial void OnIsEditingExistingChanged(bool value) => OnPropertyChanged(nameof(PinLabel));

    partial void OnSelectedRowChanged(UserRow? value)
    {
        if (value is not null)
            Edit(value.Id);
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        IsEditingExisting = false;
        IsLocked = false;
        SelectedRow = null;
        Name = "";
        SelectedRole = RoleOptions[0];
        IsActive = true;
        Pin = "";
        ClearMessage();
    }

    [RelayCommand]
    private void Save()
    {
        var role = SelectedRole?.Role ?? Role.Cashier;
        int id;
        if (_editingId is { } existing)
        {
            if (!Check(users.UpdateUser(existing, Name, role, IsActive, Pin)))
                return;
            id = existing;
        }
        else
        {
            var result = users.CreateUser(Name, Pin, role);
            if (!Check(result))
                return;
            id = result.Value!.Id;
        }

        Refresh();
        Edit(id);
        ShowInfo("Saved");
    }

    [RelayCommand]
    private void Unlock()
    {
        if (_editingId is not { } id)
            return;
        users.Unlock(id);
        Refresh();
        Edit(id);
    }

    private void Edit(int id)
    {
        var user = users.GetUsers(includeInactive: true).FirstOrDefault(u => u.Id == id);
        if (user is null)
            return;
        _editingId = user.Id;
        IsEditingExisting = true;
        Name = user.Name;
        SelectedRole = RoleOptions.First(r => r.Role == user.Role);
        IsActive = user.IsActive;
        IsLocked = user.IsLockedAt(clock.GetUtcNow().UtcDateTime);
        Pin = "";
        ClearMessage();
    }

    private void Refresh()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        Users.Clear();
        foreach (var user in users.GetUsers(includeInactive: true))
            Users.Add(new UserRow(user.Id, user.Name, L[user.IsAdmin ? "RoleAdmin" : "RoleCashier"], user.IsActive, user.IsLockedAt(now)));
    }
}
