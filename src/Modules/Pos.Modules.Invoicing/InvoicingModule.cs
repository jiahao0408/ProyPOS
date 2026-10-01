using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Invoicing;

public sealed class InvoicingModule : IModule
{
    public string Id => "FAC";

    public string NameKey => "ModuleInvoicing";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
