using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.Bazaar;
using Pos.Modules.Printing;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

public sealed partial class LabelLineViewModel(Product product, string price) : ObservableObject
{
    [ObservableProperty]
    private string _copiesText = "1";

    public Product Product { get; } = product;

    public string Name => Product.Name;

    public string Price { get; } = price;

    public string Barcode => Product.Barcode ?? "—";
}

/// <summary>BAZ-01: etiquetas de precio con código de barras, eligiendo cuántas de cada producto.</summary>
public partial class LabelsPageViewModel(
    ILocalizer localizer,
    CatalogService catalog,
    LabelService labels,
    PrintService printing,
    RegionFormatter formatter) : PageViewModel(localizer)
{
    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<Product> SearchResults { get; } = [];

    public ObservableCollection<LabelLineViewModel> Lines { get; } = [];

    /// <summary>Última impresión lanzada (para esperarla en los tests).</summary>
    public Task LastPrint { get; private set; } = Task.CompletedTask;

    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        if (value.Trim().Length < 2)
            return;
        foreach (var p in catalog.Search(value, limit: 20))
            SearchResults.Add(p);
    }

    [RelayCommand]
    private void SubmitSearch()
    {
        var text = SearchText.Trim();
        var product = catalog.FindByBarcode(text) ?? (SearchResults.Count == 1 ? SearchResults[0] : null);
        if (product is null)
            return;
        Add(product);
        SearchText = "";
    }

    [RelayCommand]
    private void AddProduct(Product product)
    {
        Add(product);
        SearchText = "";
    }

    [RelayCommand]
    private void RemoveLine(LabelLineViewModel line) => Lines.Remove(line);

    [RelayCommand]
    private void Print()
    {
        var requests = new List<LabelRequest>();
        foreach (var line in Lines)
        {
            if (!int.TryParse(line.CopiesText, out var copies) || copies < 0)
            {
                ShowError("ErrorNumberFormat");
                return;
            }
            requests.Add(new LabelRequest(line.Product.Id, copies));
        }

        var prepared = labels.Prepare(requests);
        if (!Check(prepared))
            return;

        // Los productos sin código ya tienen uno interno: se refresca la lista para verlo.
        var refreshed = Lines.Select(l => (Line: l, Product: catalog.GetProduct(l.Product.Id)!)).ToList();
        Lines.Clear();
        foreach (var (line, product) in refreshed)
            Lines.Add(new LabelLineViewModel(product, line.Price) { CopiesText = line.CopiesText });

        LastPrint = PrintAsync(prepared.Value!);
    }

    private void Add(Product product)
    {
        if (Lines.FirstOrDefault(l => l.Product.Id == product.Id) is { } existing)
        {
            existing.CopiesText = int.TryParse(existing.CopiesText, out var c) ? (c + 1).ToString(L.Culture) : "1";
            return;
        }
        Lines.Add(new LabelLineViewModel(product, formatter.FormatMoney(product.Price)));
        ClearMessage();
    }

    private async Task PrintAsync(IReadOnlyList<Pos.Core.Printing.LabelItem> items)
    {
        var outcome = await printing.PrintLabelsAsync(items);
        if (outcome.Success)
        {
            Message = string.Format(L["LabelsPrinted"], items.Sum(i => i.Copies));
            MessageIsError = false;
        }
        else
        {
            Message = string.Format(L["ErrorPrintFailed"], outcome.Error);
            MessageIsError = true;
        }
    }
}
