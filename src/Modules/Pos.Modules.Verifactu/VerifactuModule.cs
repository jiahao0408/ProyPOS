using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;
using Pos.Data;

namespace Pos.Modules.Verifactu;

public sealed class VerifactuModule : IModule
{
    public string Id => "VFA";

    public string NameKey => "ModuleVerifactu";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IInvoiceHook, VerifactuRecorder>();
        services.AddSingleton(_ => ProducerInfo.Load());
        services.AddSingleton<CertificateStore>();
        services.AddSingleton<IVerifactuTransport, HttpVerifactuTransport>();
        services.AddSingleton<VerifactuSender>();
        services.AddSingleton<VerifactuSigner>();
        services.AddSingleton<VerifactuModeService>();
        services.AddSingleton<VerifactuQueue>();
    }
}
