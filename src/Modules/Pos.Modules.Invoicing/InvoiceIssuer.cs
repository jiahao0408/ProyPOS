using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Pricing;
using Pos.Data;

namespace Pos.Modules.Invoicing;

/// <summary>Crea facturas con número correlativo y desglose de IVA (FAC-01).</summary>
internal static class InvoiceIssuer
{
    public static string SeriesFor(InvoiceType type, DateTime issuedAtUtc) =>
        type switch
        {
            InvoiceType.Simplified => "T",
            InvoiceType.Complete => "F",
            _ => "R", // FAC-03: serie propia para las rectificativas
        } + issuedAtUtc.ToLocalTime().Year.ToString("D4");

    /// <summary>La factura vigente de una venta: la completa si sustituyó al ticket (FAC-06).</summary>
    public static Invoice? CurrentInvoiceOf(PosDbContext db, int saleId)
    {
        var invoices = db.Invoices.Where(i => i.SaleId == saleId).ToList();
        var replaced = invoices.Select(i => i.ReplacesInvoiceId).OfType<int>().ToHashSet();
        return invoices.Where(i => !replaced.Contains(i.Id)).OrderByDescending(i => i.Id).FirstOrDefault();
    }

    /// <summary>
    /// Siguiente número de la serie. Mira también lo que hay pendiente de guardar en este contexto,
    /// y el índice único (serie, número) impide cualquier duplicado.
    /// </summary>
    public static int NextNumber(PosDbContext db, string series)
    {
        var saved = db.Invoices.Where(i => i.Series == series).Max(i => (int?)i.Number) ?? 0;
        var pending = db.ChangeTracker.Entries<Invoice>()
            .Where(e => e.Entity.Series == series)
            .Select(e => e.Entity.Number)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(saved, pending) + 1;
    }

    public static Invoice Create(
        PosDbContext db,
        InvoiceType type,
        Sale sale,
        IEnumerable<SaleLine> lines,
        BusinessProfile issuer,
        InvoiceCustomer? customer,
        DateTime nowUtc,
        int? replacesInvoiceId = null)
    {
        var series = SeriesFor(type, nowUtc);
        var number = NextNumber(db, series);
        return new Invoice
        {
            Type = type,
            Series = series,
            Number = number,
            Code = Invoice.FormatCode(series, number),
            IssuedAtUtc = nowUtc,
            Sale = sale.Id == 0 ? sale : null,
            SaleId = sale.Id,
            Total = sale.Total,
            IssuerName = issuer.Name,
            IssuerNif = NifValidator.Normalize(issuer.Nif),
            IssuerAddress = issuer.FullAddress,
            CustomerNif = customer is null ? null : NifValidator.Normalize(customer.Nif),
            CustomerName = customer?.Name.Trim(),
            CustomerAddress = customer?.FullAddress,
            ReplacesInvoiceId = replacesInvoiceId,
            VatLines = VatBreakdown(lines),
        };
    }

    /// <summary>Base, cuota y total por tipo de IVA; la base se calcula sobre el total de cada tipo.</summary>
    public static List<InvoiceVatLine> VatBreakdown(IEnumerable<SaleLine> lines) =>
        lines.GroupBy(l => l.VatRate)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var split = Vat.FromGross(g.Sum(l => l.LineTotal), g.Key);
                return new InvoiceVatLine { Rate = g.Key, Base = split.Base, VatAmount = split.VatAmount, Total = split.Total };
            })
            .ToList();

    /// <summary>FAC-02: el cliente de una factura completa necesita NIF válido, nombre y dirección.</summary>
    public static string? ValidateCustomer(InvoiceCustomer customer) =>
        !NifValidator.IsValid(customer.Nif) ? "ErrorCustomerNif"
        : customer.Name.Trim().Length == 0 ? "ErrorCustomerNameRequired"
        : customer.Address.Trim().Length == 0 ? "ErrorCustomerAddressRequired"
        : null;

    /// <summary>Guarda o actualiza el cliente para reutilizarlo en la próxima factura.</summary>
    public static void RememberCustomer(PosDbContext db, InvoiceCustomer customer)
    {
        var nif = NifValidator.Normalize(customer.Nif);
        var existing = db.Customers.FirstOrDefault(c => c.Nif == nif);
        if (existing is null)
        {
            db.Customers.Add(new Customer
            {
                Nif = nif,
                Name = customer.Name.Trim(),
                Address = customer.Address.Trim(),
                PostalCode = customer.PostalCode.Trim(),
                City = customer.City.Trim(),
            });
            return;
        }
        existing.Name = customer.Name.Trim();
        existing.Address = customer.Address.Trim();
        existing.PostalCode = customer.PostalCode.Trim();
        existing.City = customer.City.Trim();
    }
}

/// <summary>Al cobrar, cada venta genera su factura: simplificada o, si el cliente lo pide, completa.</summary>
public sealed class InvoiceSaleHook(ProfileStore profiles, IEnumerable<IInvoiceHook> invoiceHooks) : ISaleHook
{
    public InvoiceSaleHook(ProfileStore profiles)
        : this(profiles, [])
    {
    }

    public string? Validate(CheckoutContext context) =>
        profiles.GetBusiness().ValidateFiscalData()
        ?? (context.Customer is { } customer ? InvoiceIssuer.ValidateCustomer(customer) : null);

    public void OnSaleCreated(PosDbContext db, CheckoutContext context)
    {
        var sale = context.Sale;
        Invoice invoice;
        if (sale.Kind == SaleKind.Return)
        {
            // FAC-03: la devolución se documenta con una rectificativa que referencia la factura original
            // (y, si era una factura completa, al mismo cliente). La original no se toca.
            var original = InvoiceIssuer.CurrentInvoiceOf(db, sale.OriginalSaleId!.Value)
                ?? throw new InvalidOperationException($"La venta {sale.OriginalSaleId} no tiene factura.");
            var originalCustomer = original.CustomerNif is null ? null
                : new InvoiceCustomer(original.CustomerNif, original.CustomerName ?? "", original.CustomerAddress ?? "", "", "");
            invoice = InvoiceIssuer.Create(db, InvoiceType.Rectificative, sale, sale.Lines, profiles.GetBusiness(), originalCustomer, context.NowUtc);
            invoice.RectifiedInvoiceId = original.Id;
            invoice.CustomerAddress = original.CustomerAddress; // tal cual estaba en la original
        }
        else
        {
            var type = context.Customer is null ? InvoiceType.Simplified : InvoiceType.Complete;
            invoice = InvoiceIssuer.Create(db, type, sale, sale.Lines, profiles.GetBusiness(), context.Customer, context.NowUtc);
        }
        db.Invoices.Add(invoice);
        foreach (var hook in invoiceHooks)
            hook.OnInvoiceIssued(db, invoice);

        if (context.Customer is { } customer)
            InvoiceIssuer.RememberCustomer(db, customer);
    }
}
