using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.ViewModels;
using Pos.Core.Localization;
using Pos.Core.Modules;
using Pos.Core.Security;
using Pos.Data;
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

/// <summary>Carpetas y BD con las que arranca la app (en los tests se sustituyen).</summary>
public sealed record AppEnvironment(
    IDbContextFactory<PosDbContext> Database,
    string DataDirectory,
    string LocalesDirectory,
    TimeProvider Clock,
    DatabaseFile? DatabaseFile = null)
{
    public string PhotosDirectory => Path.Combine(DataDirectory, "photos");

    /// <summary>Carpeta de datos estándar, o la de la variable STARSEAPOS_DATA_DIR (para desarrollar y probar).</summary>
    public static AppEnvironment Default()
    {
        var dataDirectory = Program.DataDirectory;
        var file = PosDatabase.OpenFile(dataDirectory);
        return new AppEnvironment(
            file.Factory,
            dataDirectory,
            Path.Combine(AppContext.BaseDirectory, "locales"),
            TimeProvider.System,
            file);
    }
}

/// <summary>Raíz de composición: el único sitio que conoce todos los módulos.</summary>
internal static class Composition
{
    /// <param name="overrides">Solo para los tests: sustituir servicios (por ejemplo, la conexión con la AEAT).</param>
    public static IServiceProvider BuildServices(AppEnvironment env, Action<IServiceCollection>? overrides = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(env);
        services.AddSingleton(env.Database);
        services.AddSingleton(env.Clock);
        if (env.DatabaseFile is { } databaseFile)
            services.AddSingleton(databaseFile);
        services.AddSingleton<ISession, Session>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ProfileStore>();

        // CFG-01 / CFG-04: idioma y formatos guardados en los ajustes.
        services.AddSingleton<ILocalizer>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            var localizer = new JsonLocalizer(env.LocalesDirectory, defaultLanguage: "es");
            var language = settings.Get(SettingKeys.Language);
            if (language is not null && localizer.AvailableLanguages.Any(l => l.Code == language))
                localizer.SetLanguage(language);
            return localizer;
        });
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            return new RegionFormatter(sp.GetRequiredService<ILocalizer>())
            {
                CurrencySymbol = settings.Get(SettingKeys.CurrencySymbol, RegionFormatter.DefaultCurrencySymbol),
                DateFormat = settings.Get(SettingKeys.DateFormat, ""),
            };
        });

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

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<FirstRunViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<SalePageViewModel>();
        services.AddTransient<ProductsPageViewModel>();
        services.AddTransient<CategoriesPageViewModel>();
        services.AddTransient<UsersPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<TicketsPageViewModel>();
        services.AddTransient<BusinessPageViewModel>();
        services.AddTransient<PrinterPageViewModel>();
        services.AddTransient<ReceiptsPageViewModel>();
        services.AddTransient<LabelsPageViewModel>();
        services.AddTransient<DataPageViewModel>();
        services.AddTransient<VerifactuPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        services.AddTransient<AuditPageViewModel>();

        overrides?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
