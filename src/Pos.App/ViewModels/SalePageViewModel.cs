using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Localization;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

public sealed record CategoryButton(int? Id, string Name, IBrush? Background);

public sealed record ProductButton(int Id, string Name, string Price);

/// <summary>
/// Pantalla de venta. CAJ-01: con la caja cerrada solo deja abrirla.
/// PRE-02: las categorías salen como botones. El ticket y el cobro llegan en la sección 2.
/// </summary>
public partial class SalePageViewModel(
    ILocalizer localizer,
    ISession session,
    CashRegisterService cash,
    CatalogService catalog,
    RegionFormatter formatter) : PageViewModel(localizer)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCashClosed))]
    private bool _isCashOpen;

    [ObservableProperty]
    private string _openingFloatText = "";

    [ObservableProperty]
    private string _cashInfo = "";

    [ObservableProperty]
    private CategoryButton? _selectedCategory;

    public bool IsCashClosed => !IsCashOpen;

    public ObservableCollection<CategoryButton> Categories { get; } = [];

    public ObservableCollection<ProductButton> Products { get; } = [];

    public override void Load()
    {
        var session = cash.GetOpenSession();
        IsCashOpen = session is not null;
        if (session is null)
            return;

        CashInfo = string.Format(L["CashOpenSince"],
            formatter.FormatDateTime(session.OpenedAtUtc.ToLocalTime()),
            formatter.FormatMoney(session.OpeningFloat));

        Categories.Clear();
        Categories.Add(new CategoryButton(null, L["AllCategories"], null));
        foreach (var category in catalog.GetCategories())
            Categories.Add(new CategoryButton(category.Id, category.Name, ParseBrush(category.Color)));
        SelectCategory(Categories[0]);
    }

    [RelayCommand]
    private void OpenCash()
    {
        if (!formatter.TryParseAmount(OpeningFloatText, out var amount))
        {
            ShowError("ErrorAmountFormat");
            return;
        }

        if (Check(cash.Open(session.CurrentUser!.Id, amount)))
        {
            ClearMessage();
            Load();
        }
    }

    [RelayCommand]
    private void SelectCategory(CategoryButton category)
    {
        SelectedCategory = category;
        Products.Clear();
        foreach (var product in catalog.Search(null, category.Id))
            Products.Add(new ProductButton(product.Id, product.Name, formatter.FormatMoney(product.Price)));
    }

    private static IBrush? ParseBrush(string? color) =>
        color is not null && Color.TryParse(color, out var parsed) ? new SolidColorBrush(parsed) : null;
}
