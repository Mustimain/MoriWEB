using System.ComponentModel.DataAnnotations;

namespace MoriWEB.Models
{
    public class Lookup
    {
        public int Id { get; set; }
        [Required, MaxLength(64)] public string Code { get; set; } = "";
        [Required, MaxLength(128)] public string Name { get; set; } = "";
        public LookupType LookupType { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.Now;

        public ICollection<Product> Products { get; set; } = new List<Product>();

        // EKLENDİ: Kasa hareketleri ile ilişkiyi tutmak için
        public ICollection<CashTransaction> Transactions { get; set; } = new List<CashTransaction>();
    }
}
