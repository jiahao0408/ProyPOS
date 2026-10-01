using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;

namespace Pos.Data;

/// <summary>Claves de los ajustes generales de la app.</summary>
public static class SettingKeys
{
    public const string Language = "ui.language";

    /// <summary>Idioma de los tickets y documentos impresos; sin valor = el de la aplicación.</summary>
    public const string PrintLanguage = "print.language";
    public const string CurrencySymbol = "region.currencySymbol";
    public const string DateFormat = "region.dateFormat";
    public const string InactivityMinutes = "security.inactivityMinutes";

    /// <summary>CFG-06: dirección del fichero update.json con la última versión.</summary>
    public const string UpdateFeedUrl = "updates.feedUrl";
}

/// <summary>Lectura y escritura de ajustes clave-valor (tabla Settings).</summary>
public sealed class SettingsStore(IDbContextFactory<PosDbContext> dbFactory)
{
    public string? Get(string key)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefault();
    }

    public string Get(string key, string defaultValue) => Get(key) ?? defaultValue;

    public int GetInt(string key, int defaultValue) =>
        int.TryParse(Get(key), out var value) ? value : defaultValue;

    public void Set(string key, string value)
    {
        using var db = dbFactory.CreateDbContext();
        var setting = db.Settings.Find(key);
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;
        db.SaveChanges();
    }
}
