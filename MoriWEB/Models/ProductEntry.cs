using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MoriWEB.Models
{
    public class ProductEntry
    {

        public int Id { get; set; }

        [Required] public int CompanyId { get; set; }
        public Lookup? Company { get; set; }

        // Stok girişinin hangi ürüne ait olduğunu bağlamak faydalı olur:
        [Required] public int ProductId { get; set; }
        public Product? Product { get; set; }

        public double? Amount { get; set; }

        [Column(TypeName = "decimal(18,2)")] public decimal? PurchasePrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? PurchaseDiscount { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? NetPrice { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? SalesPrice { get; set; } // etiket

        public DateTime CreateDate { get; set; } = DateTime.Now;

    }
}
