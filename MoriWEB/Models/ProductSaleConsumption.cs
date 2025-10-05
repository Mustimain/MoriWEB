using System.ComponentModel.DataAnnotations.Schema;

namespace MoriWEB.Models
{
    public class ProductSaleConsumption
    {
        public int Id { get; set; }

        public int ProductSalesId { get; set; }
        public ProductSales ProductSales { get; set; } = null!;

        public int ProductEntryId { get; set; }
        public ProductEntry ProductEntry { get; set; } = null!;

        public double Quantity { get; set; } // Bu alıştan düşülen miktar

        // İstersen maliyet takibi:
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; } = 0;

        public DateTime CreateDate { get; set; } = DateTime.Now;
    }
}
