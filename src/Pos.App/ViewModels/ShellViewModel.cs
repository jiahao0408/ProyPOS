using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Data;
using Pos.Modules.Users;

namespace Pos.App.ViewModels;

/// <summary>
/// Raíz de la ventana: decide qué pantalla se ve (primer arranque, login o área de trabajo)
/// y cierra la sesión por inactividad (USR-01).
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    public const int DefaultInactivityMinutes = 5;

    private readonly IServiceProvider _services;
    private readonly ISession _session;
    private readonly UserService _users;
    private readonly SettingsStore _settings;
    private readonly TimeProvider _clock;
    private DateTimeOffset _lastActivity;

    [ObservableProperty]
    private ViewModelBase _currentScreen;

    public ShellViewModel(IServiceProvider services, ILocalizer localizer, ISession session, UserService users, SettingsStore settings, TimeProvider clock)
        : base(localizer)
    {
        _services = services;
        _session = session;
        _users = users;
        _settings = settings;
        _clock = clock;
        _lastActivity = clock.GetUtcNow();

        _currentScreen = ScreenForSession();
        _session.Changed += (_, _) =>
        {
            RegisterActivity();
            CurrentScreen = ScreenForSession();
        };
    }

    /// <summary>La ventana avisa de cada pulsación o toque.</summary>
    public void RegisterActivity() => _lastActivity = _clock.GetUtcNow();

    /// <summary>Lo llama un temporizador de la ventana; cierra la sesión si pasa el tiempo configurado sin uso.</summary>
    public void CheckInactivity()
    {
        if (_session.CurrentUser is null)
            return;

        var minutes = _settings.GetInt(SettingKeys.InactivityMinutes, DefaultInactivityMinutes);
        if (minutes > 0 && _clock.GetUtcNow() - _lastActivity >= TimeSpan.FromMinutes(minutes))
            _session.SignOut();
    }

    private ViewModelBase ScreenForSession()
    {
        if (_session.CurrentUser is not null)
            return _services.GetRequiredService<WorkspaceViewModel>();
        if (!_users.HasAnyUser())
            return _services.GetRequiredService<FirstRunViewModel>();
        return _services.GetRequiredService<LoginViewModel>();
    }
}
