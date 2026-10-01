using Microsoft.Extensions.DependencyInjection;

namespace Pos.Core.Modules;

/// <summary>
/// Contrato que cumple cada área funcional (ventas, inventario, facturas…).
/// La app descubre los módulos y les deja registrar sus servicios; el núcleo no conoce ninguno.
/// </summary>
public interface IModule
{
    /// <summary>Prefijo de las user stories del módulo: "VEN", "INV"…</summary>
    string Id { get; }

    /// <summary>Clave de traducción del nombre visible del módulo.</summary>
    string NameKey { get; }

    void ConfigureServices(IServiceCollection services);
}
