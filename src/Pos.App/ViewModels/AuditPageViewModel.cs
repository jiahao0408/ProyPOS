using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Pos.Core.Localization;
using Pos.Data;
using Pos.Localization;

namespace Pos.App.ViewModels;

public sealed record AuditRow(string When, string User, string Action, string Details);

/// <summary>USR-03: registro de acciones sensibles, de solo lectura, filtrado por día.</summary>
public partial class AuditPageViewModel(
    ILocalizer localizer,
    IDbContextFactory<PosDbContext> dbFactory,
    RegionFormatter formatter,
    TimeProvider clock) : PageViewModel(localizer)
{
    [ObservableProperty]
    private DateTime? _day;

    public ObservableCollection<AuditRow> Entries { get; } = [];

    public override void Load() => Day = clock.GetLocalNow().Date;

    partial void OnDayChanged(DateTime? value)
    {
        Entries.Clear();
        if (value is not { } day)
            return;

        var from = DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();
        var to = from.AddDays(1);
        using var db = dbFactory.CreateDbContext();
        foreach (var e in db.AuditEntries.AsNoTracking().Where(a => a.AtUtc >= from && a.AtUtc < to).OrderByDescending(a => a.Id))
        {
            var user = e.AuthorizedBy is null ? e.UserName : string.Format(L["AuditAuthorizedBy"], e.UserName, e.AuthorizedBy);
            Entries.Add(new AuditRow(formatter.FormatDateTime(e.AtUtc.ToLocalTime()), user, L[$"Audit{e.Action}"], e.Details));
        }
    }
}
