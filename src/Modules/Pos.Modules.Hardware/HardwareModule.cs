using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Hardware;

public sealed class HardwareModule : IModule
{
    public string Id => "HW";

    public string NameKey => "ModuleHardware";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
