using System.Collections.Generic;
using System.Reflection.Emit;
using System;
using Microsoft.EntityFrameworkCore;
using MoriWEB.Models;

namespace MoriWEB.DatabaseContext
{

    public class MoriDbContext : DbContext
    {
        public MoriDbContext(DbContextOptions<MoriDbContext> options) : base(options) { }

        public DbSet<Lookup> Lookups => Set<Lookup>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductEntry> ProductEntries => Set<ProductEntry>();
        public DbSet<ProductSales> ProductSales => Set<ProductSales>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();

        // YENİ: FIFO köprüsü
        public DbSet<ProductSaleConsumption> ProductSaleConsumptions => Set<ProductSaleConsumption>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // -------- Indeksler / Benzersizlik --------
            mb.Entity<Lookup>()
              .HasIndex(x => new { x.LookupType, x.Code })
              .IsUnique();

            mb.Entity<Product>()
              .HasIndex(x => x.Code)
              .IsUnique();

            mb.Entity<Customer>()
              .HasIndex(x => x.PhoneNumber)
              .IsUnique();

            // -------- Precision --------
            mb.Entity<ProductEntry>(e =>
            {
                e.Property(p => p.PurchasePrice).HasPrecision(18, 2);
                e.Property(p => p.PurchaseDiscount).HasPrecision(18, 2);
                e.Property(p => p.NetPrice).HasPrecision(18, 2);
                e.Property(p => p.SalesPrice).HasPrecision(18, 2);
                // RemainingAmount double — ekstra precision gerekmiyor
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

            // -------- İlişkiler --------
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

            // ProductEntry -> Company, Product
            mb.Entity<ProductEntry>()
              .HasOne(pe => pe.Company).WithMany().HasForeignKey(pe => pe.CompanyId)
              .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<ProductEntry>()
              .HasOne(pe => pe.Product).WithMany().HasForeignKey(pe => pe.ProductId)
              .OnDelete(DeleteBehavior.Restrict);

            // ProductSales -> Customer, ProductEntry, PaymentType
            mb.Entity<ProductSales>()
              .HasOne(ps => ps.Customer).WithMany(c => c.Sales).HasForeignKey(ps => ps.CustomerId)
              .OnDelete(DeleteBehavior.SetNull);
            mb.Entity<ProductSales>()
              .HasOne(ps => ps.ProductEntry).WithMany().HasForeignKey(ps => ps.ProductEntryId)
              .OnDelete(DeleteBehavior.SetNull);
            mb.Entity<ProductSales>()
              .HasOne(ps => ps.PaymentType).WithMany().HasForeignKey(ps => ps.PaymentTypeId)
              .OnDelete(DeleteBehavior.SetNull);

            // CashTransaction -> CashTransactionType
            mb.Entity<CashTransaction>()
              .HasOne(ct => ct.CashTransactionType).WithMany().HasForeignKey(ct => ct.CashTransactionTypeId)
              .OnDelete(DeleteBehavior.SetNull);

            // YENİ: Satış tüketimleri (FIFO)
            mb.Entity<ProductSaleConsumption>()
              .HasOne(x => x.ProductSales)
              .WithMany() // ProductSales tarafında koleksiyon tutmak istemiyorsan böyle kalsın
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
