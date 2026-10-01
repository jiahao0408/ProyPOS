using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 8: informe de ventas (CAJ-03), exportaciones (DAT-02, FAC-05) y registro de facturación (DAT-04).</summary>
public class Sprint8Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop()
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            var catalog = App.Get<CatalogService>();
            catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null));
            catalog.SaveProduct(null, new ProductInput("Libro", 10.40m, 4m, "8410000000023", null, null));
            App.Get<CashRegisterService>().Open(admin.Id, 100m);

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == "Ana"));
            foreach (var d in "1111")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public TestApp App { get; } = new();
        public WorkspaceViewModel Workspace { get; }

        public string Folder { get; } = Directory.CreateTempSubdirectory("starseapos-s8app-").FullName;

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public void Sell(string code, bool card = false)
        {
            var sale = Open<SalePageViewModel>();
            sale.SearchText = code;
            sale.SubmitSearchCommand.Execute(null);
            sale.ChargeCommand.Execute(null);
            var payment = (PaymentViewModel)sale.Dialog!;
            if (card)
                payment.SetModeCommand.Execute(PaymentMode.Card);
            payment.ConfirmCommand.Execute(null);
        }

        public void Dispose()
        {
            App.Dispose();
            Directory.Delete(Folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Report_ShowsTodaysSales()
    {
        using var shop = new Shop();
        shop.Sell("8410000000016");
        shop.Sell("8410000000023", card: true);

        var report = shop.Open<SalesReportPageViewModel>();

        Assert.Equal(("2", "13,90 €", "6,95 €"), (report.TicketsText, report.NetText.Replace(' ', ' '), report.AverageText.Replace(' ', ' ')));
        Assert.Equal(["Libro", "Taza"], report.ByProduct.Select(r => r.Name));
        Assert.Equal(["Efectivo", "Tarjeta"], report.ByPayment.Select(r => r.Name));
        Assert.Equal(("Ana", "Tickets: 2"), (report.ByCashier.Single().Name, report.ByCashier.Single().Count));

        report.YesterdayCommand.Execute(null);
        Assert.Equal("0", report.TicketsText);
        Assert.Empty(report.ByProduct);
    }

    [AvaloniaFact]
    public void Data_ExportInvoicesForTheAccountant()
    {
        using var shop = new Shop();
        shop.Sell("8410000000016");
        var data = shop.Open<DataPageViewModel>();

        data.ExportKind = data.ExportKinds.Single(k => k.Value == ExportChoice.Invoices);
        Assert.True(data.ExportNeedsDates);
        Assert.EndsWith(".xlsx", data.SuggestedExportName);
        var path = Path.Combine(shop.Folder, "facturas.xlsx");
        data.Export(path);
        Assert.Equal("Filas exportadas a facturas.xlsx: 1.", data.Message);
        Assert.True(File.Exists(path));

        data.ExportKind = data.ExportKinds.Single(k => k.Value == ExportChoice.InvoiceBookPdf);
        Assert.True(data.ExportIsPdf);
        var pdf = Path.Combine(shop.Folder, "libro.pdf");
        data.Export(pdf);
        Assert.False(data.MessageIsError, data.Message);
        Assert.True(new FileInfo(pdf).Length > 1000);

        data.ExportKind = data.ExportKinds.Single(k => k.Value == ExportChoice.Products);
        Assert.False(data.ExportNeedsDates);
        data.Export(Path.Combine(shop.Folder, "productos.csv"));
        Assert.Equal("Filas exportadas a productos.csv: 2.", data.Message);
    }

    [AvaloniaFact]
    public void Data_BillingRecordExportAndVerify()
    {
        using var shop = new Shop();
        shop.Sell("8410000000016");
        shop.Sell("8410000000023");
        var data = shop.Open<DataPageViewModel>();
        var path = Path.Combine(shop.Folder, "registro.zip");

        data.ExportBillingRecord(path);
        Assert.StartsWith("Registro exportado en registro.zip: 2 registros. Huella final: ", data.Message);

        data.VerifyBillingRecord(path);
        Assert.False(data.MessageIsError, data.Message);
        Assert.StartsWith("Exportación correcta: 2 registros", data.Message);

        var other = Path.Combine(shop.Folder, "otra-cosa.zip");
        File.WriteAllText(other, "no es un zip");
        data.VerifyBillingRecord(other);
        Assert.True(data.MessageIsError);
        Assert.Equal("El fichero no es una exportación del registro de facturación.", data.Message);
    }
}
