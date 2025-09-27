using System.ComponentModel.DataAnnotations.Schema;

namespace MoriWEB.Models
{
    public class ProductSales
    {
        public int Id { get; set; }

        // Müşteri
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }

        public int? ProductEntryId { get; set; }
        public ProductEntry? ProductEntry { get; set; }

        public double? Amount { get; set; }

        [Column(TypeName = "decimal(18,2)")] public decimal? SalesPrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? SalesDiscount { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? NetPrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? TotalPrice { get; set; }

        public int? PaymentTypeId { get; set; }
        public Lookup? PaymentType { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.Now;
        [NotMapped] public decimal? PaidAmount { get; set; }


    }
}
