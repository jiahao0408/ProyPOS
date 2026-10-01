using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Sales;

public sealed class SalesModule : IModule
{
    public string Id => "VEN";

    public string NameKey => "ModuleSales";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
