using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;
using Pos.Data;

namespace Pos.Modules.DataTransfer;

public sealed class DataTransferModule : IModule
{
    public string Id => "DAT";

    public string NameKey => "ModuleDataTransfer";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ImportService>();
        // La BD puede no ser un fichero (tests en memoria): entonces las copias no están disponibles.
        services.AddSingleton(sp => new BackupService(sp.GetService<DatabaseFile>(), sp.GetRequiredService<TimeProvider>()));
    }
}
