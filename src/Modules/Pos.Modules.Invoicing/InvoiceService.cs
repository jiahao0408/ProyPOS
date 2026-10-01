using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.Invoicing;

/// <summary>Resumen de una factura para listarla (IMP-02, FAC-06).</summary>
public sealed record InvoiceSummary(
    int Id, string Code, InvoiceType Type, DateTime IssuedAtUtc, decimal Total, int SaleId,
    string? CustomerName, bool IsReplaced, string? ReplacedByCode);

/// <summary>Consulta de facturas, factura completa a partir de un ticket (FAC-06) y clientes.</summary>
public sealed class InvoiceService(
    IDbContextFactory<PosDbContext> dbFactory,
    ProfileStore profiles,
    TimeProvider clock,
    IEnumerable<IInvoiceHook> invoiceHooks,
    SettingsStore? settings = null)
    : IInvoiceDocuments
{
    public InvoiceService(IDbContextFactory<PosDbContext> dbFactory, ProfileStore profiles, TimeProvider clock)
        : this(dbFactory, profiles, clock, [])
    {
    }

    /// <summary>Facturas de un día (hora local), las más recientes primero.</summary>
    public IReadOnlyList<InvoiceSummary> ListByDay(DateOnly day)
    {
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        using var db = dbFactory.CreateDbContext();
        return Summaries(db, db.Invoices.Where(i => i.IssuedAtUtc >= fromUtc && i.IssuedAtUtc < toUtc));
    }

    /// <summary>
    /// Busca por número. Acepta el código completo ("T2026-000012", también leído del QR del ticket)
    /// o solo el número ("12"), que se busca en las series del año actual.
    /// </summary>
    public IReadOnlyList<InvoiceSummary> Search(string text)
    {
        // QR de cotejo de Verifactu: la URL trae el número en el parámetro numserie.
        text = (VerifactuQr.InvoiceNumberFrom(text.Trim()) ?? text).Trim().ToUpperInvariant();
        if (text.Length == 0)
            return [];

        using var db = dbFactory.CreateDbContext();
        if (int.TryParse(text, out var number))
            return Summaries(db, db.Invoices.Where(i => i.Number == number));

        // El QR puede traer más datos; nos quedamos con la parte que parece un código de factura.
        var code = text.Split(['|', ' ', ';', '&', '=', '?'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(p => p.Length >= 8 && p.Contains('-') && char.IsLetter(p[0])) ?? text;
        return Summaries(db, db.Invoices.Where(i => i.Code == code));
    }

    public InvoiceDocument? Get(int invoiceId)
    {
        using var db = dbFactory.CreateDbContext();
        return ToDocument(db, invoiceId);
    }

    public InvoiceDocument? GetCurrentForSale(int saleId)
    {
        using var db = dbFactory.CreateDbContext();
        var ids = db.Invoices.Where(i => i.SaleId == saleId).Select(i => i.Id).ToList();
        if (ids.Count == 0)
            return null;
        // La vigente es la que no ha sido sustituida por otra.
        var replaced = db.Invoices.Where(i => i.ReplacesInvoiceId != null && ids.Contains(i.ReplacesInvoiceId.Value))
            .Select(i => i.ReplacesInvoiceId!.Value).ToHashSet();
        return ToDocument(db, ids.Where(id => !replaced.Contains(id)).Max());
    }

    /// <summary>
    /// FAC-06: el cliente vuelve a pedir factura de un ticket. Se emite una factura completa que
    /// sustituye a la simplificada y la referencia. La simplificada no se toca ni se borra.
    /// Un ticket solo se puede facturar una vez.
    /// </summary>
    public OperationResult<InvoiceDocument> IssueFromTicket(int simplifiedInvoiceId, InvoiceCustomer customer)
    {
        var business = profiles.GetBusiness();
        if (business.ValidateFiscalData() is { } businessError)
            return OperationResult<InvoiceDocument>.Fail(businessError);
        if (InvoiceIssuer.ValidateCustomer(customer) is { } customerError)
            return OperationResult<InvoiceDocument>.Fail(customerError);

        using var db = dbFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var original = db.Invoices.AsNoTracking().FirstOrDefault(i => i.Id == simplifiedInvoiceId);
        if (original is null)
            return OperationResult<InvoiceDocument>.Fail("ErrorInvoiceNotFound");
        if (original.Type != InvoiceType.Simplified)
            return OperationResult<InvoiceDocument>.Fail("ErrorInvoiceNotSimplified");
        if (db.Invoices.Any(i => i.ReplacesInvoiceId == original.Id))
            return OperationResult<InvoiceDocument>.Fail("ErrorTicketAlreadyInvoiced");

        var sale = db.Sales.AsNoTracking().Include(s => s.Lines).First(s => s.Id == original.SaleId);
        var invoice = InvoiceIssuer.Create(db, InvoiceType.Complete, sale, sale.Lines, business, customer,
            clock.GetUtcNow().UtcDateTime, replacesInvoiceId: original.Id);
        db.Invoices.Add(invoice);
        foreach (var hook in invoiceHooks)
            hook.OnInvoiceIssued(db, invoice);
        InvoiceIssuer.RememberCustomer(db, customer);
        db.SaveChanges();
        transaction.Commit();

        return OperationResult<InvoiceDocument>.Ok(ToDocument(db, invoice.Id)!);
    }

    public Customer? FindCustomer(string nif)
    {
        var normalized = NifValidator.Normalize(nif);
        using var db = dbFactory.CreateDbContext();
        return db.Customers.AsNoTracking().FirstOrDefault(c => c.Nif == normalized);
    }

    private static List<InvoiceSummary> Summaries(PosDbContext db, IQueryable<Invoice> query)
    {
        var invoices = query.AsNoTracking().OrderByDescending(i => i.Id).Take(500).ToList();
        var ids = invoices.Select(i => i.Id).ToList();
        var replacements = db.Invoices.AsNoTracking()
            .Where(i => i.ReplacesInvoiceId != null && ids.Contains(i.ReplacesInvoiceId.Value))
            .ToDictionary(i => i.ReplacesInvoiceId!.Value, i => i.Code);

        return invoices.Select(i => new InvoiceSummary(
                i.Id, i.Code, i.Type, i.IssuedAtUtc, i.Total, i.SaleId, i.CustomerName,
                replacements.ContainsKey(i.Id), replacements.GetValueOrDefault(i.Id)))
            .ToList();
    }

    private InvoiceDocument? ToDocument(PosDbContext db, int invoiceId)
    {
        var invoice = db.Invoices.AsNoTracking().Include(i => i.VatLines).FirstOrDefault(i => i.Id == invoiceId);
        if (invoice is null)
            return null;

        var sale = db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .First(s => s.Id == invoice.SaleId);
        var cashier = db.Users.AsNoTracking().Where(u => u.Id == sale.UserId).Select(u => u.Name).FirstOrDefault() ?? "";
        var replacesCode = invoice.ReplacesInvoiceId is { } replacedId
            ? db.Invoices.AsNoTracking().Where(i => i.Id == replacedId).Select(i => i.Code).FirstOrDefault()
            : null;

        return new InvoiceDocument(
            invoice.Id, sale.Id, invoice.Type, invoice.Code, invoice.IssuedAtUtc,
            invoice.IssuerName, invoice.IssuerNif, invoice.IssuerAddress,
            invoice.CustomerNif, invoice.CustomerName, invoice.CustomerAddress, replacesCode, cashier,
            sale.Lines.OrderBy(l => l.Id)
                .Select(l => new InvoiceDocumentLine(l.Description, l.Quantity, l.UnitPrice, l.VatRate, l.LineTotal)).ToList(),
            invoice.VatLines.OrderByDescending(v => v.Rate).ToList(),
            sale.Payments.OrderBy(p => p.Id).Select(p => new InvoiceDocumentPayment(p.Method, p.Amount)).ToList(),
            invoice.Total, sale.CashTendered, sale.Change,
            // FAC-04: QR de cotejo de la AEAT con el NIF, el número, la fecha y el importe.
            VerifactuQr.Build(invoice.IssuerNif, invoice.Code, invoice.IssuedAtUtc.ToLocalTime(), invoice.Total,
                testEnvironment: settings?.Get(VerifactuSettingKeys.Environment) != VerifactuSettingKeys.Production),
            VerifactuMode: settings?.Get(VerifactuSettingKeys.Enabled) == "true");
    }
}
