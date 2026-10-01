using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Data;

namespace Pos.App;

/// <summary>
/// CFG-02: cada usuario puede tener su idioma. Al entrar con su PIN la interfaz cambia a ese idioma;
/// sin preferencia (o al cerrar la sesión) se usa el idioma de la tienda, el de Ajustes.
/// El cambio de un usuario no afecta a los demás.
/// </summary>
public sealed class UserLanguage(ILocalizer localizer, SettingsStore settings)
{
    /// <summary>Idioma de la tienda (Ajustes); "es" si no se ha elegido.</summary>
    public string ShopLanguage => Valid(settings.Get(SettingKeys.Language)) ?? "es";

    public void Apply(User? user) => localizer.SetLanguage(Valid(user?.LanguageCode) ?? ShopLanguage);

    private string? Valid(string? code) =>
        code is { Length: > 0 } && localizer.AvailableLanguages.Any(l => l.Code == code) ? code : null;
}
