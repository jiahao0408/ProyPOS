using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

/// <summary>Entrada del menú lateral. El título se actualiza al cambiar de idioma.</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly string _titleKey;

    public NavItemViewModel(ILocalizer localizer, string titleKey, Type pageType, bool requiresAdmin)
    {
        _localizer = localizer;
        _titleKey = titleKey;
        PageType = pageType;
        RequiresAdmin = requiresAdmin;
        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public string Title => _localizer[_titleKey];

    public Type PageType { get; }

    public bool RequiresAdmin { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Petición del PIN de un administrador cuando un cajero entra en una sección de admin (USR-02).</summary>
public sealed partial class AdminPinPromptViewModel : ViewModelBase
{
    private readonly UserService _users;
    private readonly Action<User> _onAuthorized;
    private readonly Action _onCancel;

    /// <param name="onAuthorized">Recibe el administrador que ha puesto su PIN (para la auditoría, USR-03).</param>
    public AdminPinPromptViewModel(ILocalizer localizer, UserService users, Action<User> onAuthorized, Action onCancel)
        : base(localizer)
    {
        _users = users;
        _onAuthorized = onAuthorized;
        _onCancel = onCancel;
        PinEntry.Completed += Submit;
    }

    public PinEntryViewModel PinEntry { get; } = new();

    [RelayCommand]
    private void Cancel() => _onCancel();

    private void Submit(string pin)
    {
        if (_users.VerifyAdminPin(pin) is not { } admin)
        {
            ShowError("AdminPinWrong");
            return;
        }
        _onAuthorized(admin);
    }
}

/// <summary>Pantalla principal tras el login: menú lateral, barra superior y página actual.</summary>
public partial class WorkspaceViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ISession _session;
    private readonly UserService _users;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdminPromptOpen))]
    private AdminPinPromptViewModel? _adminPrompt;

    public WorkspaceViewModel(IServiceProvider services, ILocalizer localizer, ISession session, UserService users)
        : base(localizer)
    {
        _services = services;
        _session = session;
        _users = users;

        NavItems =
        [
            new NavItemViewModel(localizer, "NavSale", typeof(SalePageViewModel), requiresAdmin: false),
            new NavItemViewModel(localizer, "NavTickets", typeof(TicketsPageViewModel), requiresAdmin: false),
            new NavItemViewModel(localizer, "NavLabels", typeof(LabelsPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavProducts", typeof(ProductsPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavCategories", typeof(CategoriesPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavUsers", typeof(UsersPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavBusiness", typeof(BusinessPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavPrinter", typeof(PrinterPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavData", typeof(DataPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavVerifactu", typeof(VerifactuPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavAudit", typeof(AuditPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavSettings", typeof(SettingsPageViewModel), requiresAdmin: true),
            new NavItemViewModel(localizer, "NavAbout", typeof(AboutPageViewModel), requiresAdmin: false),
        ];
        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(RoleTitle));
        Show(NavItems[0]);
    }

    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    public string UserName => _session.CurrentUser?.Name ?? "";

    public string RoleTitle => L[_session.IsAdmin ? "RoleAdmin" : "RoleCashier"];

    public bool IsAdminPromptOpen => AdminPrompt is not null;

    [RelayCommand]
    private void Navigate(NavItemViewModel item)
    {
        if (item.RequiresAdmin && !_session.IsAdmin)
        {
            // USR-02: el cajero necesita el PIN de un admin; la autorización vale solo para esta visita.
            AdminPrompt = new AdminPinPromptViewModel(L, _users,
                onAuthorized: _ =>
                {
                    AdminPrompt = null;
                    Show(item);
                },
                onCancel: () => AdminPrompt = null);
            return;
        }
        Show(item);
    }

    [RelayCommand]
    private void Logout() => _session.SignOut();

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private void Show(NavItemViewModel item)
    {
        foreach (var nav in NavItems)
            nav.IsSelected = nav == item;

        var page = (PageViewModel)_services.GetRequiredService(item.PageType);
        page.Load();
        CurrentPage = page;
    }
}
