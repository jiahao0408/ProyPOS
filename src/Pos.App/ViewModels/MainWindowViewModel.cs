using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Modules;

namespace Pos.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyList<IModule> _modules;

    public MainWindowViewModel(ILocalizer localizer, IEnumerable<IModule> modules)
    {
        L = localizer;
        _modules = modules.ToList();
        _selectedLanguage = Languages.First(l => l.Code == localizer.CurrentLanguage);

        L.LanguageChanged += (_, _) => RefreshModuleNames();
        RefreshModuleNames();
    }

    /// <summary>Textos traducidos para la vista: {Binding L[Clave]}.</summary>
    public ILocalizer L { get; }

    public IReadOnlyList<LanguageInfo> Languages => L.AvailableLanguages;

    public ObservableCollection<string> ModuleNames { get; } = [];

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value is not null)
            L.SetLanguage(value.Code);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void RefreshModuleNames()
    {
        ModuleNames.Clear();
        foreach (var module in _modules)
            ModuleNames.Add($"{module.Id} · {L[module.NameKey]}");
    }
}
