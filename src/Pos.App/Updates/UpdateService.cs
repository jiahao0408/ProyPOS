using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Pos.Data;
using Pos.Modules.DataTransfer;

namespace Pos.App.Updates;

/// <summary>Contenido de update.json: la última versión publicada y su instalador.</summary>
public sealed record UpdateInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <param name="Update">La versión nueva, si hay una.</param>
public sealed record UpdateCheckResult(UpdateInfo? Update, string? ErrorKey = null, string? ErrorDetail = null);

/// <summary>Lanza el instalador y cierra la app. Separado para poder probar sin instalar nada.</summary>
public interface IInstallerLauncher
{
    void Launch(string msiPath);
}

public sealed class MsiexecLauncher : IInstallerLauncher
{
    /// <summary>
    /// msiexec con interfaz reducida: el MSI (MajorUpgrade) sustituye la versión instalada sin tocar los datos.
    /// Las migraciones de la base de datos se aplican solas al arrancar la versión nueva.
    /// </summary>
    public void Launch(string msiPath)
    {
        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\" /passive /norestart") { UseShellExecute = true });
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}

/// <summary>
/// CFG-06: actualizaciones automáticas. Al arrancar se mira si hay versión nueva (update.json) y se avisa.
/// Al instalar: se descarga el MSI, se comprueba su SHA-256, se hace una copia de seguridad de la base de
/// datos y se lanza el instalador. La versión nueva migra la base de datos sola al arrancar.
/// </summary>
public sealed class UpdateService(SettingsStore settings, BackupService backups, AppEnvironment environment,
    IInstallerLauncher launcher, Func<HttpClient> httpClient)
{
    public const string DefaultFeedUrl = "https://github.com/jiahao0408/ProyPOS/releases/latest/download/update.json";

    public Version CurrentVersion { get; init; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Versión nueva encontrada en la última comprobación.</summary>
    public UpdateInfo? Available { get; private set; }

    public event Action<UpdateInfo>? UpdateFound;

    public string FeedUrl => settings.Get(SettingKeys.UpdateFeedUrl) is { Length: > 0 } url ? url : DefaultFeedUrl;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        UpdateInfo? info;
        try
        {
            using var client = httpClient();
            info = await client.GetFromJsonAsync<UpdateInfo>(FeedUrl, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return new UpdateCheckResult(null, "ErrorUpdateCheck", e.Message);
        }

        if (info is null || !System.Version.TryParse(info.Version, out var version) || !Uri.TryCreate(info.Url, UriKind.Absolute, out _))
            return new UpdateCheckResult(null, "ErrorUpdateCheck", "update.json");
        if (Normalize(version) <= Normalize(CurrentVersion))
        {
            Available = null;
            return new UpdateCheckResult(null);
        }

        Available = info;
        UpdateFound?.Invoke(info);
        return new UpdateCheckResult(info);
    }

    /// <summary>Descarga y comprueba el instalador, hace la copia de seguridad y lo lanza.</summary>
    public async Task<UpdateCheckResult> DownloadAndInstallAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(environment.DataDirectory, "updates");
        Directory.CreateDirectory(folder);
        var msi = Path.Combine(folder, $"StarSeaPOS-{update.Version}.msi");
        try
        {
            using var client = httpClient();
            await using (var download = await client.GetStreamAsync(update.Url, cancellationToken))
            await using (var file = File.Create(msi))
                await download.CopyToAsync(file, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return new UpdateCheckResult(update, "ErrorUpdateDownload", e.Message);
        }

        // Un instalador que no es el publicado (descarga a medias o manipulada) no se ejecuta.
        await using (var file = File.OpenRead(msi))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (!hash.Equals(update.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                file.Close();
                File.Delete(msi);
                return new UpdateCheckResult(update, "ErrorUpdateChecksum", Path.GetFileName(msi));
            }
        }

        try
        {
            backups.CreatePreUpdateCopy(update.Version);
        }
        catch (Exception e) when (e is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            return new UpdateCheckResult(update, "ErrorUpdateBackup", e.Message);
        }

        launcher.Launch(msi);
        return new UpdateCheckResult(update);
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
