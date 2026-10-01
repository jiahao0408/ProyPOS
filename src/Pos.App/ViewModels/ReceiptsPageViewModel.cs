using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Localization;
using Pos.Modules.Inventory;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

/// <summary>Una línea de la entrada, editable: cantidad, cajas o unidades, y coste.</summary>
public sealed partial class ReceiptLineViewModel : ObservableObject
{
    private readonly ILocalizer _l;
    private readonly RegionFormatter _formatter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitsText), nameof(CostLabel))]
    private bool _inBoxes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitsText))]
    private string _quantityText = "1";

    [ObservableProperty]
    private string _costText;

    public ReceiptLineViewModel(Product product, ILocalizer l, RegionFormatter formatter)
    {
        Product = product;
        _l = l;
        _formatter = formatter;
        // BAZ-04: si el producto viene en cajas, por defecto se recibe por cajas.
        _inBoxes = product.UnitsPerBox > 1;
        _costText = product.CostPrice > 0
            ? formatter.FormatAmount(_inBoxes ? product.CostPrice * product.UnitsPerBox : product.CostPrice)
            : "";
    }

    public Product Product { get; }

    public string Name => Product.Name;

    public bool CanUseBoxes => Product.UnitsPerBox > 1;

    public string BoxInfo => string.Format(_l["BoxOf"], Product.UnitsPerBox);

    public string CostLabel => _l[InBoxes ? "CostPerBox" : "CostPerUnit"];

    /// <summary>Unidades que entrarán en stock.</summary>
    public string UnitsText => int.TryParse(QuantityText, out var q) && q > 0
        ? string.Format(_l["UnitsToStock"], InBoxes ? q * Product.UnitsPerBox : q)
        : "";

    public ReceiptLineInput? ToInput() =>
        int.TryParse(QuantityText, out var quantity) && _formatter.TryParseAmount(CostText.Length == 0 ? "0" : CostText, out var cost)
            ? new ReceiptLineInput(Product.Id, quantity, InBoxes && CanUseBoxes, cost)
            : null;
}

/// <summary>INV-03: entradas de mercancía con proveedor, cantidad y coste. BAZ-04: por cajas.</summary>
public partial class ReceiptsPageViewModel(
    ILocalizer localizer,
    ISession session,
    ReceiptService receipts,
    CatalogService catalog,
    RegionFormatter formatter) : PageViewModel(localizer)
{
    [ObservableProperty]
    private Supplier? _selectedSupplier;

    [ObservableProperty]
    private string _newSupplierName = "";

    [ObservableProperty]
    private string _reference = "";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    private ViewModelBase? _dialog;

    public ObservableCollection<Supplier> Suppliers { get; } = [];

    public ObservableCollection<ReceiptLineViewModel> Lines { get; } = [];

    public ObservableCollection<Product> SearchResults { get; } = [];

    public bool IsDialogOpen => Dialog is not null;

    public override void Load()
    {
        Suppliers.Clear();
        foreach (var s in receipts.GetSuppliers())
            Suppliers.Add(s);
    }

    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        if (value.Trim().Length < 2)
            return;
        foreach (var p in catalog.Search(value, limit: 20))
            SearchResults.Add(p);
    }

    [RelayCommand]
    private void AddSupplier()
    {
        var result = receipts.CreateSupplier(NewSupplierName);
        if (!Check(result))
            return;
        Load();
        SelectedSupplier = Suppliers.First(s => s.Id == result.Value!.Id);
        NewSupplierName = "";
        ClearMessage();
    }

    /// <summary>Enter en el buscador o escaneo: añade el producto; si el código no existe, alta rápida.</summary>
    [RelayCommand]
    private void SubmitSearch()
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
            return;

        if (catalog.FindByBarcode(text) is { } product)
            AddLine(product);
        else if (SearchResults.Count == 1)
            AddLine(SearchResults[0]);
        else if (text.Length >= 4 && text.All(char.IsAsciiDigit))
            OpenQuickCreate(text);
        else
            return;
        SearchText = "";
    }

    [RelayCommand]
    private void AddProduct(Product product)
    {
        AddLine(product);
        SearchText = "";
    }

    [RelayCommand]
    private void RemoveLine(ReceiptLineViewModel line) => Lines.Remove(line);

    [RelayCommand]
    private void Save()
    {
        var inputs = Lines.Select(l => l.ToInput()).ToList();
        if (inputs.Any(i => i is null))
        {
            ShowError("ErrorNumberFormat");
            return;
        }

        var result = receipts.Receive(SelectedSupplier?.Id, Reference, inputs!, session.CurrentUser!.Id);
        if (!Check(result))
            return;

        var units = result.Value!.Lines.Sum(l => l.Units);
        Lines.Clear();
        Reference = "";
        Message = string.Format(L["ReceiptSaved"], units, formatter.FormatMoney(result.Value.TotalCost));
        MessageIsError = false;
    }

    private void AddLine(Product product)
    {
        // Si ya está en la entrada, se suma una más en lugar de repetir la línea.
        if (Lines.FirstOrDefault(l => l.Product.Id == product.Id) is { } existing)
        {
            existing.QuantityText = int.TryParse(existing.QuantityText, out var q) ? (q + 1).ToString(L.Culture) : "1";
            return;
        }
        Lines.Add(new ReceiptLineViewModel(catalog.GetProduct(product.Id) ?? product, L, formatter));
        ClearMessage();
    }

    /// <summary>BAZ-03 también en la recepción: el código no existe y se da de alta sin parar.</summary>
    private void OpenQuickCreate(string barcode)
    {
        Dialog = new QuickCreateViewModel(L, formatter, barcode, catalog.GetCategories(), createdByCashier: false,
            save: (name, price, sectionId) =>
            {
                var created = catalog.QuickCreate(name, price, sectionId, barcode, createdByAdmin: true);
                if (!created.Success)
                    return created;
                Dialog = null;
                AddLine(created.Value!);
                return OperationResult.Ok();
            },
            cancel: () => Dialog = null);
    }
}
