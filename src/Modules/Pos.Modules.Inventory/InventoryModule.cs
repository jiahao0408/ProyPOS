using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Inventory;

public sealed class InventoryModule : IModule
{
    public string Id => "INV";

    public string NameKey => "ModuleInventory";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<StockService>();
    }
}
