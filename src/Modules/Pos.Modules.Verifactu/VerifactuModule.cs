using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Verifactu;

public sealed class VerifactuModule : IModule
{
    public string Id => "VFA";

    public string NameKey => "ModuleVerifactu";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
