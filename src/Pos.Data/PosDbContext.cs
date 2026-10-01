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
            e.Ignore(s => s.IsOpen);
            e.HasIndex(s => s.ClosedAtUtc);
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
        });

        model.Entity<SaleLine>(e =>
        {
            e.Property(l => l.Description).HasMaxLength(200);
            e.Property(l => l.UnitPrice).HasPrecision(10, 2);
            e.Property(l => l.VatRate).HasPrecision(5, 2);
            e.Property(l => l.LineTotal).HasPrecision(10, 2);
            e.HasIndex(l => l.ProductId);
            e.HasIndex(l => l.CategoryId);
            e.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(l => l.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Payment>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(10, 2);
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
