using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.CashRegister;

public sealed class CashRegisterModule : IModule
{
    public string Id => "CAJ";

    public string NameKey => "ModuleCashRegister";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
