using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MoriWEB.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required] public int ProductSalesId { get; set; }
        public ProductSales? ProductSales { get; set; }

        [Column(TypeName = "decimal(18,2)")] public decimal? PaymentAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? RemainingBalance { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.Now;
        [MaxLength(512)] public string? Description { get; set; }

    }
}
