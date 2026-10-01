using Microsoft.Extensions.DependencyInjection;
using Pos.App.ViewModels;
using Pos.Core.Localization;
using Pos.Core.Modules;
using Pos.Localization;
using Pos.Modules.Bazaar;
using Pos.Modules.CashRegister;
using Pos.Modules.DataTransfer;
using Pos.Modules.Hardware;
using Pos.Modules.Inventory;
using Pos.Modules.Invoicing;
using Pos.Modules.Printing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;
using Pos.Modules.Verifactu;

namespace Pos.App;

/// <summary>Raíz de composición: el único sitio que conoce todos los módulos.</summary>
internal static class Composition
{
    public static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        var localesDirectory = Path.Combine(AppContext.BaseDirectory, "locales");
        services.AddSingleton<ILocalizer>(new JsonLocalizer(localesDirectory, defaultLanguage: "es"));

        IModule[] modules =
        [
            new UsersModule(),
            new ProductsModule(),
            new CashRegisterModule(),
            new SalesModule(),
            new InventoryModule(),
            new PrintingModule(),
            new InvoicingModule(),
            new HardwareModule(),
            new BazaarModule(),
            new DataTransferModule(),
            new VerifactuModule(),
        ];
        foreach (var module in modules)
        {
            services.AddSingleton(module);
            module.ConfigureServices(services);
        }

        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
