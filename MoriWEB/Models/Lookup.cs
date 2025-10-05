using System.ComponentModel.DataAnnotations;

namespace MoriWEB.Models
{
    public class Lookup
    {
        public int Id { get; set; }

        [Required, MaxLength(64)]
        public string Code { get; set; } = "";

        [Required, MaxLength(128)]
        public string Name { get; set; } = "";

        public LookupType LookupType { get; set; }

        public DateTime CreateDate { get; set; } = DateTime.Now;

        // YENİ: 0=Kasa Çıkışı(−), 1=Kasa Girişi(+). Diğer türlerde 0 kalır.
        public int TransactionSign { get; set; } = 0;

        // (İsteğe bağlı navigation’lar)
        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<CashTransaction> Transactions { get; set; } = new List<CashTransaction>();
    }
}
