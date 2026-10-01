using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Printing;

public sealed class PrintingModule : IModule
{
    public string Id => "IMP";

    public string NameKey => "ModulePrinting";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ReceiptBuilder>();
        services.AddSingleton<ReportBuilder>();
        services.AddSingleton<PrintService>();
    }
}
