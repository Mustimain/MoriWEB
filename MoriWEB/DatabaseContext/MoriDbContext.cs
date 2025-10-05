using Microsoft.EntityFrameworkCore;
using MoriWEB.Models;

namespace MoriWEB.DatabaseContext
{
    public class MoriDbContext : DbContext
    {
        public MoriDbContext(DbContextOptions<MoriDbContext> options) : base(options) { }

        // ===== DbSets =====
        public DbSet<Lookup> Lookups => Set<Lookup>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductEntry> ProductEntries => Set<ProductEntry>();
        public DbSet<ProductSales> ProductSales => Set<ProductSales>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
        public DbSet<ProductSaleConsumption> ProductSaleConsumptions => Set<ProductSaleConsumption>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // ===== Indeksler / Benzersizlik =====
            mb.Entity<Lookup>()
              .HasIndex(x => new { x.LookupType, x.Code })
              .IsUnique();

            mb.Entity<Product>()
              .HasIndex(x => x.Code)
              .IsUnique();

            mb.Entity<Customer>()
              .HasIndex(x => x.PhoneNumber)
              .IsUnique();

            // ===== Lookup: TransactionSign (0/1) =====
            // 0 => stok girişi (−), 1 => stok çıkışı (+)
            mb.Entity<Lookup>(e =>
            {
                // Kolon tipini belirtmeyin, int olarak gitsin
                e.Property(x => x.TransactionSign)
                 .HasDefaultValue(0);

                // CHECK ifadesi: hem MySQL hem SQL Server’da geçerli
                e.ToTable(t => t.HasCheckConstraint(
                    "CK_Lookup_TransactionSign",
                    "TransactionSign IN (0,1)"
                ));
            });

            // ===== Precision / Kolon Ayarları =====
            mb.Entity<ProductEntry>(e =>
            {
                e.Property(p => p.PurchasePrice).HasPrecision(18, 2);
                e.Property(p => p.PurchaseDiscount).HasPrecision(18, 2);
                e.Property(p => p.NetPrice).HasPrecision(18, 2);
                e.Property(p => p.SalesPrice).HasPrecision(18, 2);
            });

            mb.Entity<ProductSales>(e =>
            {
                e.Property(p => p.SalesPrice).HasPrecision(18, 2);
                e.Property(p => p.SalesDiscount).HasPrecision(18, 2);
                e.Property(p => p.NetPrice).HasPrecision(18, 2);
                e.Property(p => p.TotalPrice).HasPrecision(18, 2);
            });

            mb.Entity<CashTransaction>(e =>
            {
                e.Property(p => p.Amount).HasPrecision(18, 2);
            });

            // ===== İlişkiler =====
            // Product -> Lookup'lar
            mb.Entity<Product>()
              .HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId)
              .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<Product>()
              .HasOne(p => p.Brand).WithMany().HasForeignKey(p => p.BrandId)
              .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<Product>()
              .HasOne(p => p.FabricType).WithMany().HasForeignKey(p => p.FabricTypeId)
              .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<Product>()
              .HasOne(p => p.Model).WithMany().HasForeignKey(p => p.ModelId)
              .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<Product>()
              .HasOne(p => p.Color).WithMany().HasForeignKey(p => p.ColorId)
              .OnDelete(DeleteBehavior.Restrict);

            // ProductEntry -> Company (Lookup:Company), Product
            mb.Entity<ProductEntry>()
              .HasOne(pe => pe.Company).WithMany().HasForeignKey(pe => pe.CompanyId)
              .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<ProductEntry>()
              .HasOne(pe => pe.Product).WithMany().HasForeignKey(pe => pe.ProductId)
              .OnDelete(DeleteBehavior.Restrict);

            // ProductSales -> Customer, ProductEntry, PaymentType (Lookup:PaymentType)
            mb.Entity<ProductSales>()
              .HasOne(ps => ps.Customer).WithMany(c => c.Sales).HasForeignKey(ps => ps.CustomerId)
              .OnDelete(DeleteBehavior.SetNull);
            mb.Entity<ProductSales>()
              .HasOne(ps => ps.ProductEntry).WithMany().HasForeignKey(ps => ps.ProductEntryId)
              .OnDelete(DeleteBehavior.SetNull);
            mb.Entity<ProductSales>()
              .HasOne(ps => ps.PaymentType).WithMany().HasForeignKey(ps => ps.PaymentTypeId)
              .OnDelete(DeleteBehavior.SetNull);

            // CashTransaction -> Lookup (CashTransactionType)
            // CashTransactionType artık Lookups içinde (LookupType.CashTransactionType)
            mb.Entity<CashTransaction>()
              .HasOne(ct => ct.CashTransactionType)
              .WithMany(l => l.Transactions)
              .HasForeignKey(ct => ct.CashTransactionTypeId)
              .OnDelete(DeleteBehavior.SetNull);

            // Satış tüketimleri (FIFO köprüsü)
            mb.Entity<ProductSaleConsumption>()
              .HasOne(x => x.ProductSales)
              .WithMany()
              .HasForeignKey(x => x.ProductSalesId)
              .OnDelete(DeleteBehavior.Cascade);

            mb.Entity<ProductSaleConsumption>()
              .HasOne(x => x.ProductEntry)
              .WithMany()
              .HasForeignKey(x => x.ProductEntryId)
              .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
