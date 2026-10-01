using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;

namespace Pos.Data;

public class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.Property(u => u.Name).HasMaxLength(100);
            e.Property(u => u.LanguageCode).HasMaxLength(10);
        });

        model.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.Color).HasMaxLength(7);
        });

        model.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Price).HasPrecision(10, 2);
            e.Property(p => p.VatRate).HasPrecision(5, 2);
            e.Property(p => p.Barcode).HasMaxLength(64);
            e.HasIndex(p => p.Barcode).IsUnique(); // PRE-01; SQLite admite varios NULL en un índice único
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
        });
    }
}
