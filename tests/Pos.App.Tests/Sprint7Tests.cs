using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Localization;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>
/// Recorridos de la sección 7: variantes (BAZ-05), verificador de precios (BAZ-06), cambio masivo
/// de precios (PRE-03) e idioma de impresión de los tickets.
/// </summary>
public class Sprint7Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop(bool asAdmin = true)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            users.CreateUser("Luis", "2222", Role.Cashier);
            var catalog = App.Get<CatalogService>();
            var ropa = catalog.SaveCategory(null, "Ropa", 0, null, false).Value!;
            Camiseta = catalog.SaveProduct(null, new ProductInput("Camiseta", 9.95m, 21m, null, ropa.Id, null, "Pasillo 2")).Value!;
            catalog.SaveVariant(Camiseta.Id, null, "Roja M", "8410000000108", null);
            catalog.SaveVariant(Camiseta.Id, null, "Roja XL", "8410000000115", 11.95m);
            Taza = catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null)).Value!;
            App.Get<CashRegisterService>().Open(admin.Id, 100m);

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == (asAdmin ? "Ana" : "Luis")));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public TestApp App { get; } = new();
        public Product Camiseta { get; }
        public Product Taza { get; }
        public WorkspaceViewModel Workspace { get; }

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public void Dispose() => App.Dispose();
    }

    [AvaloniaFact]
    public void Sale_ProductWithVariants_AsksWhichOne()
    {
        // BAZ-05: el producto sale una vez; al tocarlo se elige la variante.
        using var shop = new Shop(asAdmin: false);
        var sale = shop.Open<SalePageViewModel>();
        var button = sale.Products.Single(p => p.Name == "Camiseta");
        Assert.True(button.HasVariants);
        Assert.DoesNotContain(sale.Products, p => p.Name.Contains('·')); // las variantes no salen sueltas

        sale.AddProductCommand.Execute(button);
        var picker = Assert.IsType<VariantPickerViewModel>(sale.Dialog);
        Assert.Equal(["Roja M", "Roja XL"], picker.Variants.Select(v => v.Name));
        picker.PickCommand.Execute(picker.Variants[1]);

        Assert.False(sale.IsDialogOpen);
        var line = Assert.Single(sale.TicketLines);
        Assert.Equal(("Camiseta · Roja XL", "11,95 €"), (line.Description, line.Total.Replace(' ', ' ')));
    }

    [AvaloniaFact]
    public void Sale_ScanningAVariant_AddsItDirectly()
    {
        using var shop = new Shop(asAdmin: false);
        var sale = shop.Open<SalePageViewModel>();

        sale.SearchText = "8410000000108";
        sale.SubmitSearchCommand.Execute(null);

        Assert.False(sale.IsDialogOpen);
        Assert.Equal("Camiseta · Roja M", Assert.Single(sale.TicketLines).Description);
    }

    [AvaloniaFact]
    public void PriceCheck_ShowsPriceLocationAndVariants_WithoutAddingToTicket()
    {
        // BAZ-06.
        using var shop = new Shop(asAdmin: false);
        var sale = shop.Open<SalePageViewModel>();

        sale.CheckPriceCommand.Execute(null);
        var check = Assert.IsType<PriceCheckViewModel>(sale.Dialog);
        check.SearchText = "8410000000115";
        check.SubmitCommand.Execute(null);

        Assert.Equal(("Camiseta · Roja XL", "11,95 €", "Pasillo 2", "Ropa"),
            (check.ProductName, check.PriceText.Replace(' ', ' '), check.LocationText, check.CategoryText));
        Assert.Equal(["Roja M", "Roja XL"], check.Variants.Select(v => v.Name));

        check.SearchText = "8419999999999";
        check.SubmitCommand.Execute(null);
        Assert.False(check.HasProduct);
        Assert.Equal("Código 8419999999999 desconocido", check.Message);

        check.CloseCommand.Execute(null);
        Assert.False(sale.IsDialogOpen);
        Assert.Empty(sale.TicketLines);
    }

    [AvaloniaFact]
    public void Products_AddAndEditVariants()
    {
        using var shop = new Shop();
        var page = shop.Open<ProductsPageViewModel>();
        Assert.DoesNotContain(page.Products, p => p.Name.Contains('·'));
        page.SelectedRow = page.Products.Single(p => p.Name == "Camiseta");
        Assert.Equal("Pasillo 2", page.Location);
        Assert.Equal(2, page.Variants.Count);
        Assert.Equal([false, true], page.Variants.Select(v => v.OwnPrice));

        page.VariantName = "Azul S";
        page.VariantBarcode = "8410000000122";
        page.SaveVariantCommand.Execute(null);
        Assert.Equal("Guardado.", page.VariantMessage);

        page.SelectedVariant = page.Variants.Single(v => v.Name == "Roja XL");
        Assert.Equal("11,95", page.VariantPriceText);
        page.VariantPriceText = "";                     // vuelve al precio común
        page.SaveVariantCommand.Execute(null);

        var catalog = shop.App.Get<CatalogService>();
        Assert.Equal([9.95m, 9.95m, 9.95m], catalog.GetVariants(shop.Camiseta.Id).Select(v => v.Price));
        Assert.Equal("Camiseta · Azul S", catalog.FindByBarcode("8410000000122")!.Name);
    }

    [AvaloniaFact]
    public void Prices_PreviewThenApply_IsAudited()
    {
        // PRE-03 + USR-03.
        using var shop = new Shop();
        var page = shop.Open<PricesPageViewModel>();
        page.SelectedCategory = page.CategoryOptions.Single(c => c.Name == "Ropa");
        page.ValueText = "10";

        page.PreviewCommand.Execute(null);
        Assert.Equal("Precios que cambian: 3", page.PreviewInfo);
        Assert.Equal(("Camiseta", "9,95 €", "10,95 €"),
            (page.PreviewRows[0].Name, page.PreviewRows[0].OldPrice.Replace(' ', ' '), page.PreviewRows[0].NewPrice.Replace(' ', ' ')));
        Assert.Equal(9.95m, shop.App.Get<CatalogService>().GetProduct(shop.Camiseta.Id)!.Price);

        page.ApplyCommand.Execute(null);

        Assert.Equal("Precios actualizados: 3 productos.", page.Message);
        Assert.False(page.HasPreview);
        Assert.Equal(10.95m, shop.App.Get<CatalogService>().GetProduct(shop.Camiseta.Id)!.Price);
        Assert.Equal(3.50m, shop.App.Get<CatalogService>().GetProduct(shop.Taza.Id)!.Price); // otra categoría
        var audit = shop.Open<AuditPageViewModel>();
        Assert.Equal(("Cambio de precio", "+10 % · Ropa · 3 productos"), (Assert.Single(audit.Entries).Action, audit.Entries[0].Details));
    }

    [AvaloniaFact]
    public void Prices_AreOnlyForAdmins()
    {
        using var shop = new Shop(asAdmin: false);

        shop.Workspace.NavigateCommand.Execute(shop.Workspace.NavItems.Single(n => n.PageType == typeof(PricesPageViewModel)));

        Assert.IsType<AdminPinPromptViewModel>(shop.Workspace.AdminPrompt);
    }

    [AvaloniaFact]
    public void Settings_PrintLanguageIsIndependentFromTheAppLanguage()
    {
        using var shop = new Shop();
        var settings = shop.Open<SettingsPageViewModel>();
        Assert.Equal("", settings.SelectedPrintLanguage!.Code); // por defecto, el de la aplicación

        settings.SelectedLanguage = settings.Languages.Single(l => l.Code == "zh");
        settings.SelectedPrintLanguage = settings.PrintLanguages.Single(l => l.Code == "es");
        settings.SaveCommand.Execute(null);

        Assert.Equal("zh", shop.App.Get<Pos.Core.Localization.ILocalizer>().CurrentLanguage);
        var (print, _) = shop.App.Get<PrintLocalization>().For();
        Assert.Equal("es", print.CurrentLanguage);
        Assert.Equal("FACTURA SIMPLIFICADA", print["InvoiceSimplified"]);
        Assert.Equal("es", settings.SelectedPrintLanguage!.Code);
    }
}
