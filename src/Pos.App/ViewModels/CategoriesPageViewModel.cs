using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

/// <summary>PRE-02: categorías (secciones del bazar) que salen como botones en la venta.</summary>
public partial class CategoriesPageViewModel(ILocalizer localizer, CatalogService catalog) : PageViewModel(localizer)
{
    private int? _editingId;

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _sortOrderText = "0";

    [ObservableProperty]
    private string _color = "";

    [ObservableProperty]
    private bool _allowsGenericSale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isEditingExisting;

    /// <summary>Borrar pide dos pulsaciones: la primera solo avisa.</summary>
    [ObservableProperty]
    private bool _isDeletePending;

    public bool CanDelete => IsEditingExisting;

    public ObservableCollection<Category> Categories { get; } = [];

    public override void Load()
    {
        Refresh();
        New();
    }

    partial void OnSelectedCategoryChanged(Category? value)
    {
        if (value is null)
            return;
        _editingId = value.Id;
        IsEditingExisting = true;
        IsDeletePending = false;
        Name = value.Name;
        SortOrderText = value.SortOrder.ToString(L.Culture);
        Color = value.Color ?? "";
        AllowsGenericSale = value.AllowsGenericSale;
        ClearMessage();
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        IsEditingExisting = false;
        IsDeletePending = false;
        SelectedCategory = null;
        Name = "";
        SortOrderText = (Categories.Count == 0 ? 0 : Categories.Max(c => c.SortOrder) + 1).ToString(L.Culture);
        Color = "";
        AllowsGenericSale = false;
        ClearMessage();
    }

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(SortOrderText, out var sortOrder))
        {
            ShowError("ErrorNumberFormat");
            return;
        }

        var result = catalog.SaveCategory(_editingId, Name, sortOrder, Color, AllowsGenericSale);
        if (!Check(result))
            return;

        var id = result.Value!.Id;
        Refresh();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == id);
        ShowInfo("Saved");
    }

    [RelayCommand]
    private void Delete()
    {
        if (_editingId is not { } id)
            return;
        if (!IsDeletePending)
        {
            IsDeletePending = true;
            ShowError("ConfirmDeleteCategory");
            return;
        }

        catalog.DeleteCategory(id);
        Refresh();
        New();
    }

    private void Refresh()
    {
        Categories.Clear();
        foreach (var category in catalog.GetCategories())
            Categories.Add(category);
    }
}
