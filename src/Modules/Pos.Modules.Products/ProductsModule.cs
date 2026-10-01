using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Products;

public sealed class ProductsModule : IModule
{
    public string Id => "PRE";

    public string NameKey => "ModuleProducts";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<CatalogService>();
        services.AddSingleton<PriceService>();
    }
}
