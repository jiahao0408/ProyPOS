using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.Verifactu;

public enum VerifactuMode
{
    /// <summary>Los registros se envían a la AEAT al momento.</summary>
    VeriFactu,

    /// <summary>Los registros no se envían: se firman y se conservan, con registro de eventos.</summary>
    NoVeriFactu,
}

/// <summary>
/// VFA-04: modalidad VERI*FACTU o No VERI*FACTU. El cambio queda registrado (auditoría y registro de
/// eventos). Quien ha enviado registros este año en VERI*FACTU no puede volver a No VERI*FACTU hasta el
/// año siguiente (art. 3.2 del Reglamento: la opción por VERI*FACTU vale para todo el año natural).
/// </summary>
public sealed class VerifactuModeService(
    IDbContextFactory<PosDbContext> dbFactory,
    SettingsStore settings,
    CertificateStore certificates,
    TimeProvider clock)
{
    public VerifactuMode Mode =>
        settings.Get(VerifactuSettingKeys.Enabled) == "true" ? VerifactuMode.VeriFactu : VerifactuMode.NoVeriFactu;

    public OperationResult SetMode(VerifactuMode mode, User? user)
    {
        if (mode == Mode)
            return OperationResult.Ok();
        if (mode == VerifactuMode.VeriFactu && !certificates.HasCertificate)
            return OperationResult.Fail("ErrorVerifactuNoCertificate");

        using var db = dbFactory.CreateDbContext();
        if (mode == VerifactuMode.NoVeriFactu)
        {
            var yearStart = new DateTime(clock.GetLocalNow().Year, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
            if (db.VerifactuRecords.Any(r => r.AnsweredAtUtc >= yearStart))
                return OperationResult.Fail("ErrorVerifactuModeLocked");
        }

        settings.Set(VerifactuSettingKeys.Enabled, mode == VerifactuMode.VeriFactu ? "true" : "false");
        var now = clock.GetUtcNow().UtcDateTime;
        var details = mode == VerifactuMode.VeriFactu ? "No VERI*FACTU → VERI*FACTU" : "VERI*FACTU → No VERI*FACTU";
        AuditLog.Record(db, user, AuditActions.VerifactuMode, details, now);
        VerifactuEventLog.Record(db, VerifactuEventTypes.ModeChanged, details, now, user?.Name);
        db.SaveChanges();
        return OperationResult.Ok();
    }

    /// <summary>En No VERI*FACTU, cada arranque del sistema queda en el registro de eventos.</summary>
    public void RecordStartup(string version)
    {
        if (Mode != VerifactuMode.NoVeriFactu)
            return;
        using var db = dbFactory.CreateDbContext();
        VerifactuEventLog.Record(db, VerifactuEventTypes.Startup, $"StarSeaPOS {version}", clock.GetUtcNow().UtcDateTime);
        db.SaveChanges();
    }

    public IReadOnlyList<VerifactuEvent> GetEvents(int limit = 200)
    {
        using var db = dbFactory.CreateDbContext();
        return db.VerifactuEvents.AsNoTracking().OrderByDescending(e => e.Id).Take(limit).ToList();
    }

    /// <summary>Comprueba la cadena del registro de eventos. Null = intacta; si no, el Id del primer evento alterado.</summary>
    public int? VerifyEvents()
    {
        using var db = dbFactory.CreateDbContext();
        return VerifactuEventLog.FirstBroken(db.VerifactuEvents.AsNoTracking().OrderBy(e => e.Id));
    }
}
