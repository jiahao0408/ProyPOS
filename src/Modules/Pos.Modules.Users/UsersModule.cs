using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Modules;

namespace Pos.Modules.Users;

public sealed class UsersModule : IModule
{
    public string Id => "USR";

    public string NameKey => "ModuleUsers";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<UserService>();
    }
}
