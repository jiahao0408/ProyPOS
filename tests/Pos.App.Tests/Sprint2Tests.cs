using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Modules.CashRegister;
using Pos.Modules.Inventory;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 2 (vender y cobrar) con la interfaz real.</summary>
public class Sprint2Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop(bool asAdmin = false)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            var cashier = users.CreateUser("Luis", "2222", Role.Cashier).Value!;
            var catalog = App.Get<CatalogService>();
            Hogar = catalog.SaveCategory(null, "Hogar", 0, "#F59E0B", allowsGenericSale: true).Value!;
            Taza = catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000011", Hogar.Id, null)).Value!;
            Libro = catalog.SaveProduct(null, new ProductInput("Libro de cuentos", 10.40m, 4m, "8410000000028", null, null)).Value!;
            App.Get<CashRegisterService>().Open(admin.Id, 100m);

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Id == (asAdmin ? admin.Id : cashier.Id)));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
            Sale = Assert.IsType<SalePageViewModel>(Workspace.CurrentPage);
        }

        public TestApp App { get; } = new();
        public Category Hogar { get; }
        public Product Taza { get; }
        public Product Libro { get; }
        public WorkspaceViewModel Workspace { get; }
        public SalePageViewModel Sale { get; }

        public void Scan(string code)
        {
            Sale.SearchText = code;
            Sale.SubmitSearchCommand.Execute(null);
        }

        public void Dispose() => App.Dispose();
    }

    [AvaloniaFact]
    public void Scan_AddsOneUnitPerScan()
    {
        // HW-04: un escaneo añade 1 unidad.
        using var shop = new Shop();

        shop.Scan("8410000000011");
        shop.Scan("8410000000011");

        var line = Assert.Single(shop.Sale.TicketLines);
        Assert.Equal(("Taza", 2), (line.Description, line.Quantity));
        Assert.Equal("7,00 €", shop.Sale.TotalText.Replace(' ', ' '));
        Assert.Equal("", shop.Sale.SearchText);
    }

    [AvaloniaFact]
    public void Scan_WithQuantityPrefix()
    {
        using var shop = new Shop();

        shop.Scan("3*8410000000011");

        Assert.Equal(3, Assert.Single(shop.Sale.TicketLines).Quantity);
    }

    [AvaloniaFact]
    public void Search_ByNameAndTap()
    {
        // VEN-01: añadir por búsqueda.
        using var shop = new Shop();

        shop.Sale.SearchText = "cuentos";
        var result = Assert.Single(shop.Sale.Products);
        shop.Sale.AddProductCommand.Execute(result);

        Assert.Equal("Libro de cuentos", Assert.Single(shop.Sale.TicketLines).Description);
        Assert.Contains("IVA 4 %", shop.Sale.VatSummary);
    }

    [AvaloniaFact]
    public void Category_ShowsItsProducts()
    {
        // VEN-01: añadir por categoría.
        using var shop = new Shop();

        shop.Sale.SelectCategoryCommand.Execute(shop.Sale.Categories.Single(c => c.Name == "Hogar"));

        Assert.Equal("Taza", Assert.Single(shop.Sale.Products).Name);
    }

    [AvaloniaFact]
    public void Lines_ChangeQuantityAndRemove()
    {
        // VEN-02: cambiar cantidades y quitar líneas; el total se actualiza.
        using var shop = new Shop();
        shop.Scan("8410000000011");
        shop.Scan("8410000000028");

        shop.Sale.IncreaseLineCommand.Execute(shop.Sale.TicketLines[0]);
        Assert.Equal(2, shop.Sale.TicketLines[0].Quantity);

        shop.Sale.RemoveLineCommand.Execute(shop.Sale.TicketLines[1]);
        Assert.Single(shop.Sale.TicketLines);
        Assert.Equal("7,00 €", shop.Sale.TotalText.Replace(' ', ' '));

        shop.Sale.DecreaseLineCommand.Execute(shop.Sale.TicketLines[0]);
        shop.Sale.DecreaseLineCommand.Execute(shop.Sale.TicketLines[0]);
        Assert.Empty(shop.Sale.TicketLines);
    }

    [AvaloniaFact]
    public void CashPayment_ShowsChangeAndDecrementsStock()
    {
        // VEN-03 + INV-01.
        using var shop = new Shop();
        shop.Scan("3*8410000000011");

        shop.Sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);
        payment.CashTenderedText = "20";
        Assert.Equal("9,50 €", payment.ChangeText.Replace(' ', ' '));
        payment.ConfirmCommand.Execute(null);

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Empty(shop.Sale.TicketLines);
        Assert.Contains("Cambio: 9,50 €", shop.Sale.Message!.Replace(' ', ' '));
        Assert.Equal(-3, shop.App.Get<StockService>().GetStock(shop.Taza.Id));
    }

    [AvaloniaFact]
    public void CashPayment_RejectsLessThanTotal()
    {
        using var shop = new Shop();
        shop.Scan("8410000000028");
        shop.Sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);

        payment.CashTenderedText = "5";
        payment.ConfirmCommand.Execute(null);

        Assert.True(shop.Sale.IsDialogOpen);
        Assert.Equal("El efectivo entregado no llega al total.", payment.Message);
        Assert.Single(shop.Sale.TicketLines);
    }

    [AvaloniaFact]
    public void CashPayment_EmptyTenderedMeansExact()
    {
        using var shop = new Shop();
        shop.Scan("8410000000011");
        shop.Sale.ChargeCommand.Execute(null);

        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);
        payment.ConfirmCommand.Execute(null);

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Contains("Cambio: 0,00 €", shop.Sale.Message!.Replace(' ', ' '));
    }

    [AvaloniaFact]
    public void MixedPayment_RecordsBothMethods()
    {
        // VEN-04: pago mixto.
        using var shop = new Shop();
        shop.Scan("8410000000028"); // 10,40
        shop.Sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);

        payment.SetModeCommand.Execute(PaymentMode.Mixed);
        payment.CardAmountText = "6,40";
        Assert.Equal("4,00 €", payment.CashDueText.Replace(' ', ' '));
        payment.CashTenderedText = "5";
        payment.ConfirmCommand.Execute(null);

        var sale = shop.App.Get<SalesService>().GetSale(1)!;
        Assert.Equal(6.40m, sale.Payments.Single(p => p.Method == PaymentMethod.Card).Amount);
        Assert.Equal(4.00m, sale.Payments.Single(p => p.Method == PaymentMethod.Cash).Amount);
        Assert.Equal(1.00m, sale.Change);
    }

    [AvaloniaFact]
    public void CardPayment()
    {
        using var shop = new Shop();
        shop.Scan("8410000000011");
        shop.Sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);

        payment.SetModeCommand.Execute(PaymentMode.Card);
        payment.ConfirmCommand.Execute(null);

        var only = Assert.Single(shop.App.Get<SalesService>().GetSale(1)!.Payments);
        Assert.Equal((PaymentMethod.Card, 3.50m), (only.Method, only.Amount));
    }

    [AvaloniaFact]
    public void Charge_EmptyTicketShowsError()
    {
        using var shop = new Shop();

        shop.Sale.ChargeCommand.Execute(null);

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Equal("El ticket está vacío.", shop.Sale.Message);
    }

    [AvaloniaFact]
    public void GenericItem_BySection()
    {
        // BAZ-02: artículo genérico tecleando el importe y la sección.
        using var shop = new Shop();
        var hogar = Assert.Single(shop.Sale.GenericSections);

        shop.Sale.AddGenericCommand.Execute(hogar);
        var generic = Assert.IsType<GenericItemViewModel>(shop.Sale.Dialog);
        generic.AmountText = "4,95";
        generic.AddCommand.Execute(null);

        var line = Assert.Single(shop.Sale.TicketLines);
        Assert.Equal("Hogar", line.Description);
        Assert.Null(line.Line.Item.ProductId);
        Assert.Equal(shop.Hogar.Id, line.Line.Item.CategoryId);
    }

    [AvaloniaFact]
    public void UnknownBarcode_CashierQuickCreatesPendingProduct()
    {
        // HW-04: código desconocido muestra aviso. BAZ-03: alta rápida, pendiente de revisión.
        using var shop = new Shop();

        shop.Scan("2*8419999999999");
        var quick = Assert.IsType<QuickCreateViewModel>(shop.Sale.Dialog);
        Assert.Equal("Código 8419999999999 desconocido", quick.UnknownBarcodeText);
        Assert.True(quick.CreatedByCashier);

        quick.Name = "Pelota";
        quick.PriceText = "2,50";
        quick.SelectedSection = quick.Sections.Single(s => s.Name == "Hogar");
        quick.SaveCommand.Execute(null);

        Assert.False(shop.Sale.IsDialogOpen);
        var line = Assert.Single(shop.Sale.TicketLines);
        Assert.Equal(("Pelota", 2), (line.Description, line.Quantity));
        var created = shop.App.Get<CatalogService>().FindByBarcode("8419999999999")!;
        Assert.True(created.PendingReview);
        Assert.Equal(shop.Hogar.Id, created.CategoryId);
    }

    [AvaloniaFact]
    public void UnknownBarcode_AdminQuickCreateIsNotPending()
    {
        using var shop = new Shop(asAdmin: true);

        shop.Scan("8419999999999");
        var quick = Assert.IsType<QuickCreateViewModel>(shop.Sale.Dialog);
        quick.Name = "Pelota";
        quick.PriceText = "2,50";
        quick.SaveCommand.Execute(null);

        Assert.False(shop.App.Get<CatalogService>().FindByBarcode("8419999999999")!.PendingReview);
    }

    [AvaloniaFact]
    public void Stock_IsShownToCashier()
    {
        // INV-02: muestra unidades disponibles (el cajero no tiene dónde editarlas).
        using var shop = new Shop();

        shop.Sale.SearchText = "Taza";

        Assert.Equal("Stock: 0", Assert.Single(shop.Sale.Products).Stock);
    }

    [AvaloniaFact]
    public void Shortcut_F12OpensPayment()
    {
        using var shop = new Shop();
        var window = shop.App.ShowWindow();
        shop.Scan("8410000000011");

        window.VisibleTexts();
        window.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);

        Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);
    }

    [AvaloniaFact]
    public void Scanner_TypingOutsideSearchBoxGoesToSearch()
    {
        // HW-04: el lector escribe como un teclado; aunque el foco esté en otro sitio, el código llega al buscador.
        using var shop = new Shop();
        var window = shop.App.ShowWindow();
        window.VisibleTexts();
        window.FocusManager?.ClearFocus();

        window.KeyTextInput("8410000000011");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.Equal("Taza", Assert.Single(shop.Sale.TicketLines).Description);
    }
}
