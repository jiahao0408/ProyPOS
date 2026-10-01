using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Invoicing;
using Pos.Core.Modules;
using Pos.Data;

namespace Pos.Modules.Invoicing;

public sealed class InvoicingModule : IModule
{
    public string Id => "FAC";

    public string NameKey => "ModuleInvoicing";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<InvoiceService>();
        services.AddSingleton<IInvoiceDocuments>(sp => sp.GetRequiredService<InvoiceService>());
        services.AddSingleton<ISaleHook, InvoiceSaleHook>();
        services.AddSingleton<InvoicePdf>();
    }
}
