using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Bazaar;

public sealed class BazaarModule : IModule
{
    public string Id => "BAZ";

    public string NameKey => "ModuleBazaar";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
