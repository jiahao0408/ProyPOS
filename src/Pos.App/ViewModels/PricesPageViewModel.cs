using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Localization;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

public sealed record PriceChangeRow(string Name, string OldPrice, string NewPrice);

/// <summary>
/// PRE-03: cambio de precios de varios productos a la vez. Se elige la categoría (o todas),
/// porcentaje o importe (negativo = bajada), se revisa la vista previa y se aplica.
/// </summary>
public partial class PricesPageViewModel(
    ILocalizer localizer,
    ISession session,
    CatalogService catalog,
    PriceService prices,
    RegionFormatter formatter) : PageViewModel(localizer)
{
    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private bool _isPercent = true;

    [ObservableProperty]
    private string _valueText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _previewInfo = "";

    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];

    public ObservableCollection<PriceChangeRow> PreviewRows { get; } = [];

    public bool HasPreview => PreviewRows.Count > 0;

    public bool IsAmount
    {
        get => !IsPercent;
        set => IsPercent = !value;
    }

    partial void OnIsPercentChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAmount));
        ClearPreview();
    }

    partial void OnSelectedCategoryChanged(CategoryOption? value) => ClearPreview();

    partial void OnValueTextChanged(string value) => ClearPreview();

    public override void Load()
    {
        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryOption(null, L["AllCategories"]));
        foreach (var category in catalog.GetCategories())
            CategoryOptions.Add(new CategoryOption(category.Id, category.Name));
        SelectedCategory = CategoryOptions[0];
        ValueText = "";
        ClearPreview();
        ClearMessage();
    }

    [RelayCommand]
    private void Preview()
    {
        if (!TryReadValue(out var value))
            return;
        var result = prices.Preview(SelectedCategory?.Id, Mode, value);
        if (!Check(result))
            return;

        ClearMessage();
        PreviewRows.Clear();
        foreach (var line in result.Value!)
            PreviewRows.Add(new PriceChangeRow(line.Name, formatter.FormatMoney(line.OldPrice), formatter.FormatMoney(line.NewPrice)));
        PreviewInfo = string.Format(L["PriceChangePreviewInfo"], result.Value!.Count(l => l.NewPrice != l.OldPrice));
        OnPropertyChanged(nameof(HasPreview));
    }

    /// <summary>Solo se aplica lo que se ha visto en la vista previa.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (!HasPreview || !TryReadValue(out var value))
            return;
        var result = prices.Apply(SelectedCategory?.Id, Mode, value, session.CurrentUser!.Id);
        if (!Check(result))
            return;

        ClearPreview();
        ValueText = "";
        Message = string.Format(L["PriceChangeDone"], result.Value);
        MessageIsError = false;
    }

    private PriceChangeMode Mode => IsPercent ? PriceChangeMode.Percent : PriceChangeMode.Amount;

    private bool TryReadValue(out decimal value)
    {
        if (formatter.TryParseAmount(ValueText.Replace("%", "").Replace("+", ""), out value))
            return true;
        ShowError("ErrorNumberFormat");
        return false;
    }

    private void ClearPreview()
    {
        PreviewRows.Clear();
        PreviewInfo = "";
        OnPropertyChanged(nameof(HasPreview));
    }
}
