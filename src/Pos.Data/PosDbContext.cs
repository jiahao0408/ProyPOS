using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;

namespace Pos.Data;

public class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceVatLine> InvoiceVatLines => Set<InvoiceVatLine>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<VerifactuRecord> VerifactuRecords => Set<VerifactuRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.Property(u => u.Name).HasMaxLength(100);
            e.Property(u => u.LanguageCode).HasMaxLength(10);
            e.HasIndex(u => u.Name).IsUnique();
            e.Ignore(u => u.IsAdmin);
        });

        model.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.Color).HasMaxLength(7);
            e.HasIndex(c => c.Name).IsUnique();
        });

        model.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Price).HasPrecision(10, 2);
            e.Property(p => p.VatRate).HasPrecision(5, 2);
            e.Property(p => p.CostPrice).HasPrecision(12, 4);
            e.Property(p => p.Barcode).HasMaxLength(64);
            e.HasIndex(p => p.Barcode).IsUnique(); // PRE-01; SQLite admite varios NULL en un índice único
            e.HasIndex(p => p.Name);
            e.HasIndex(p => p.PendingReview);
            e.ToTable(t => t.HasCheckConstraint("CK_Product_UnitsPerBox", "UnitsPerBox >= 1"));
            e.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<CashSession>(e =>
        {
            e.Property(s => s.OpeningFloat).HasPrecision(10, 2);
            e.Property(s => s.CountedCash).HasPrecision(10, 2);
            e.Property(s => s.ExpectedCash).HasPrecision(10, 2);
            e.Property(s => s.SalesTotal).HasPrecision(10, 2);
            e.Property(s => s.CashTotal).HasPrecision(10, 2);
            e.Property(s => s.CardTotal).HasPrecision(10, 2);
            e.Ignore(s => s.IsOpen);
            e.Ignore(s => s.Difference);
            e.HasIndex(s => s.ClosedAtUtc);
            e.HasIndex(s => s.ZNumber).IsUnique();
        });

        model.Entity<VerifactuRecord>(e =>
        {
            e.Property(r => r.IssuerNif).HasMaxLength(20);
            e.Property(r => r.IssuerName).HasMaxLength(200);
            e.Property(r => r.InvoiceNumber).HasMaxLength(60);
            e.Property(r => r.IssueDate).HasMaxLength(10);
            e.Property(r => r.InvoiceType).HasMaxLength(2);
            e.Property(r => r.TotalVat).HasPrecision(12, 2);
            e.Property(r => r.Total).HasPrecision(12, 2);
            e.Property(r => r.PreviousHash).HasMaxLength(64);
            e.Property(r => r.GeneratedAt).HasMaxLength(25);
            e.Property(r => r.Hash).HasMaxLength(64);
            e.Property(r => r.ErrorCode).HasMaxLength(20);
            e.Property(r => r.ErrorMessage).HasMaxLength(1000);
            e.Property(r => r.Environment).HasMaxLength(20);
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.InvoiceId);
            e.HasIndex(r => r.Hash).IsUnique();
            e.HasIndex(r => r.PreviousRecordId).IsUnique(); // la cadena no tiene ramas
            e.HasOne(r => r.Invoice).WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.PreviousRecord).WithMany().HasForeignKey(r => r.PreviousRecordId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.UserName).HasMaxLength(100);
            e.Property(a => a.AuthorizedBy).HasMaxLength(100);
            e.Property(a => a.Action).HasMaxLength(40);
            e.Property(a => a.Details).HasMaxLength(2000);
            e.HasIndex(a => a.AtUtc);
            e.HasIndex(a => a.Action);
        });

        model.Entity<Supplier>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200);
            e.Property(s => s.Nif).HasMaxLength(20);
            e.Property(s => s.Phone).HasMaxLength(40);
            e.HasIndex(s => s.Name).IsUnique();
        });

        model.Entity<GoodsReceipt>(e =>
        {
            e.Property(r => r.Reference).HasMaxLength(100);
            e.Property(r => r.TotalCost).HasPrecision(12, 2);
            e.HasIndex(r => r.CreatedAtUtc);
            e.HasOne<Supplier>().WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<GoodsReceiptLine>(e =>
        {
            e.Property(l => l.UnitCost).HasPrecision(12, 4);
            e.Property(l => l.LineCost).HasPrecision(12, 2);
            e.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(100);
        });

        model.Entity<Sale>(e =>
        {
            e.Property(s => s.Total).HasPrecision(10, 2);
            e.Property(s => s.CashTendered).HasPrecision(10, 2);
            e.Property(s => s.Change).HasPrecision(10, 2);
            e.HasIndex(s => s.CreatedAtUtc);
            e.HasIndex(s => s.CashSessionId);
            e.HasOne<CashSession>().WithMany().HasForeignKey(s => s.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(s => s.Payments).WithOne().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.Reason).HasMaxLength(300);
            e.Property(s => s.DiscountPercent).HasPrecision(5, 2);
            e.HasIndex(s => s.OriginalSaleId);
            e.HasOne<Sale>().WithMany().HasForeignKey(s => s.OriginalSaleId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<SaleLine>(e =>
        {
            e.Property(l => l.Description).HasMaxLength(200);
            e.Property(l => l.UnitPrice).HasPrecision(10, 2);
            e.Property(l => l.VatRate).HasPrecision(5, 2);
            e.Property(l => l.LineTotal).HasPrecision(10, 2);
            e.Property(l => l.Discount).HasPrecision(10, 2);
            e.HasIndex(l => l.OriginalLineId);
            e.HasOne<SaleLine>().WithMany().HasForeignKey(l => l.OriginalLineId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => l.ProductId);
            e.HasIndex(l => l.CategoryId);
            e.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(l => l.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Payment>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(10, 2);
        });

        model.Entity<Invoice>(e =>
        {
            e.Property(i => i.Series).HasMaxLength(10);
            e.Property(i => i.Code).HasMaxLength(20);
            e.Property(i => i.Total).HasPrecision(10, 2);
            e.Property(i => i.IssuerName).HasMaxLength(200);
            e.Property(i => i.IssuerNif).HasMaxLength(20);
            e.Property(i => i.IssuerAddress).HasMaxLength(400);
            e.Property(i => i.CustomerNif).HasMaxLength(20);
            e.Property(i => i.CustomerName).HasMaxLength(200);
            e.Property(i => i.CustomerAddress).HasMaxLength(400);
            e.HasIndex(i => new { i.Series, i.Number }).IsUnique(); // FAC-01: sin números repetidos
            e.HasIndex(i => i.Code).IsUnique();
            e.HasIndex(i => i.IssuedAtUtc);
            e.HasIndex(i => i.SaleId);
            e.HasIndex(i => i.ReplacesInvoiceId).IsUnique(); // FAC-06: un ticket solo se factura una vez
            e.HasIndex(i => i.RectifiedInvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(i => i.RectifiedInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Sale).WithMany().HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Invoice>().WithMany().HasForeignKey(i => i.ReplacesInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(i => i.VatLines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<InvoiceVatLine>(e =>
        {
            e.Property(l => l.Rate).HasPrecision(5, 2);
            e.Property(l => l.Base).HasPrecision(10, 2);
            e.Property(l => l.VatAmount).HasPrecision(10, 2);
            e.Property(l => l.Total).HasPrecision(10, 2);
        });

        model.Entity<Customer>(e =>
        {
            e.Property(c => c.Nif).HasMaxLength(20);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Address).HasMaxLength(300);
            e.Property(c => c.PostalCode).HasMaxLength(10);
            e.Property(c => c.City).HasMaxLength(100);
            e.HasIndex(c => c.Nif).IsUnique();
        });

        model.Entity<StockMovement>(e =>
        {
            e.Property(m => m.Note).HasMaxLength(500);
            e.HasIndex(m => new { m.ProductId, m.CreatedAtUtc });
            e.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Sale).WithMany().HasForeignKey(m => m.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
