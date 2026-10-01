using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Localization;

namespace Pos.App.ViewModels;

public abstract partial class ViewModelBase(ILocalizer localizer) : ObservableObject
{
    /// <summary>Textos traducidos para la vista: {Binding L[Clave]}.</summary>
    public ILocalizer L { get; } = localizer;

    /// <summary>Mensaje de error o de confirmación ya traducido; null = no hay mensaje.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsError;

    public bool HasMessage => Message is not null;

    protected void ShowError(string errorKey)
    {
        Message = L[errorKey];
        MessageIsError = true;
    }

    protected void ShowInfo(string key)
    {
        Message = L[key];
        MessageIsError = false;
    }

    protected void ClearMessage() => Message = null;

    /// <summary>Cierra la app (la ventana va a pantalla completa y no tiene botón de cerrar).</summary>
    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Muestra el error si la operación falló. Devuelve true si fue bien.</summary>
    protected bool Check(OperationResult result)
    {
        if (result.Success)
            return true;
        ShowError(result.ErrorKey!);
        return false;
    }
}

/// <summary>Página del área de trabajo (venta, productos…).</summary>
public abstract class PageViewModel(ILocalizer localizer) : ViewModelBase(localizer)
{
    /// <summary>Carga los datos de la página justo antes de mostrarla.</summary>
    public virtual void Load()
    {
    }
}
