using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.DataTransfer;

public sealed class DataTransferModule : IModule
{
    public string Id => "DAT";

    public string NameKey => "ModuleDataTransfer";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
