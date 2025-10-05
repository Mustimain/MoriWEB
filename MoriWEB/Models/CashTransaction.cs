using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MoriWEB.Models
{
    public class CashTransaction
    {
        public int Id { get; set; }

        // TransactionType KALDIRILDI — yön yeni tablodan geliyor
        public int? ProductSalesId { get; set; }
        public ProductSales? ProductSales { get; set; }

        public int? ProductEntryId { get; set; }
        public ProductEntry? ProductEntry { get; set; }

        public int? CashTransactionTypeId { get; set; }
        public Lookup? CashTransactionType { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Amount { get; set; }

        [MaxLength(512)]
        public string? Description { get; set; }

        public DateTime CreateDate { get; set; } = DateTime.Now;
    }
}
