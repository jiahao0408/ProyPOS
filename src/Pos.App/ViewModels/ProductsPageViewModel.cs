using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Pricing;
using Pos.Localization;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

public sealed record ProductRow(int Id, string Name, string Price, string Barcode, string Category, bool IsActive, bool PendingReview);

public sealed record CategoryOption(int? Id, string Name);

public sealed record VatOption(decimal Rate, string Title);

/// <summary>PRE-01: alta y edición de productos, con búsqueda rápida sobre el catálogo.</summary>
public partial class ProductsPageViewModel(
    ILocalizer localizer,
    CatalogService catalog,
    RegionFormatter formatter,
    AppEnvironment environment) : PageViewModel(localizer)
{
    private int? _editingId;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    private ProductRow? _selectedRow;

    [ObservableProperty]
    private string _resultsInfo = "";

    // --- Ficha ---

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _priceText = "";

    [ObservableProperty]
    private VatOption? _selectedVat;

    [ObservableProperty]
    private string _barcode = "";

    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private string _unitsPerBoxText = "1";

    [ObservableProperty]
    private string? _photoPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private Bitmap? _photo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleActive))]
    private bool _isEditingExisting;

    [ObservableProperty]
    private bool _editingIsActive = true;

    public ObservableCollection<ProductRow> Products { get; } = [];

    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];

    public IReadOnlyList<VatOption> VatOptions { get; } =
        VatRates.All.Select(r => new VatOption(r, $"{r:0.##} %")).ToList();

    public bool HasPhoto => Photo is not null;

    public bool CanToggleActive => IsEditingExisting;

    partial void OnPhotoPathChanged(string? value)
    {
        Photo?.Dispose();
        Photo = LoadBitmap(value);
    }

    private static Bitmap? LoadBitmap(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;
        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            return null; // imagen dañada o formato no soportado: se muestra sin foto
        }
    }

    public override void Load()
    {
        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryOption(null, L["NoCategory"]));
        foreach (var category in catalog.GetCategories())
            CategoryOptions.Add(new CategoryOption(category.Id, category.Name));

        RefreshList();
        New();
    }

    partial void OnSearchTextChanged(string value) => RefreshList();

    partial void OnShowInactiveChanged(bool value) => RefreshList();

    partial void OnSelectedRowChanged(ProductRow? value)
    {
        if (value is not null)
            Edit(value.Id);
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        IsEditingExisting = false;
        EditingIsActive = true;
        Name = "";
        PriceText = "";
        SelectedVat = VatOptions[0];
        Barcode = "";
        SelectedCategory = CategoryOptions.FirstOrDefault();
        UnitsPerBoxText = "1";
        PhotoPath = null;
        SelectedRow = null;
        ClearMessage();
    }

    [RelayCommand]
    private void Save()
    {
        if (!formatter.TryParseAmount(PriceText, out var price))
        {
            ShowError("ErrorAmountFormat");
            return;
        }
        if (!int.TryParse(UnitsPerBoxText, out var unitsPerBox))
        {
            ShowError("ErrorNumberFormat");
            return;
        }

        var input = new ProductInput(Name, price, SelectedVat?.Rate ?? -1, Barcode, SelectedCategory?.Id, PhotoPath, unitsPerBox);
        var result = catalog.SaveProduct(_editingId, input);
        if (!Check(result))
            return;

        var savedId = result.Value!.Id;
        RefreshList();
        Edit(savedId);
        ShowInfo("Saved");
    }

    [RelayCommand]
    private void ToggleActive()
    {
        if (_editingId is not { } id)
            return;
        catalog.SetProductActive(id, !EditingIsActive);
        RefreshList();
        Edit(id);
    }

    [RelayCommand]
    private void RemovePhoto() => PhotoPath = null;

    /// <summary>Copia la foto elegida a la carpeta de datos de la app y la asigna a la ficha.</summary>
    public void SetPhotoFromFile(string sourcePath)
    {
        Directory.CreateDirectory(environment.PhotosDirectory);
        var destination = Path.Combine(environment.PhotosDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, destination);
        PhotoPath = destination;
    }

    private void Edit(int id)
    {
        var product = catalog.GetProduct(id);
        if (product is null)
            return;

        _editingId = product.Id;
        IsEditingExisting = true;
        EditingIsActive = product.IsActive;
        Name = product.Name;
        PriceText = formatter.FormatAmount(product.Price);
        SelectedVat = VatOptions.FirstOrDefault(v => v.Rate == product.VatRate);
        Barcode = product.Barcode ?? "";
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Id == product.CategoryId) ?? CategoryOptions.FirstOrDefault();
        UnitsPerBoxText = product.UnitsPerBox.ToString(L.Culture);
        PhotoPath = product.PhotoPath;
        ClearMessage();
    }

    private void RefreshList()
    {
        var products = catalog.Search(SearchText, includeInactive: ShowInactive);
        Products.Clear();
        foreach (var p in products)
            Products.Add(ToRow(p));

        ResultsInfo = products.Count >= CatalogService.DefaultSearchLimit
            ? string.Format(L["SearchLimited"], products.Count)
            : string.Format(L["SearchResults"], products.Count);
    }

    private ProductRow ToRow(Product p) => new(
        p.Id, p.Name, formatter.FormatMoney(p.Price), p.Barcode ?? "", p.Category?.Name ?? "", p.IsActive, p.PendingReview);
}
