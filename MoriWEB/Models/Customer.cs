using System.ComponentModel.DataAnnotations;

namespace MoriWEB.Models
{
    public class Customer
    {
        public int Id { get; set; }

        [MaxLength(11)] public string? TcNo { get; set; }
        [MaxLength(64)] public string? FirstName { get; set; }
        [MaxLength(64)] public string? LastName { get; set; }
        [MaxLength(256)] public string? Adress { get; set; }
        [MaxLength(32)] public string? PhoneNumber { get; set; }
        [MaxLength(128)] public string? EmailAdress { get; set; }
        [MaxLength(256)] public string? CargoAdress { get; set; }
        [MaxLength(128)] public string? TaxCompanyName { get; set; }
        [MaxLength(64)] public string? TaxOffice { get; set; }
        [MaxLength(16)] public string? TaxNo { get; set; }
        [MaxLength(256)] public string? TaxAdress { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.Now;
        public ICollection<ProductSales> Sales { get; set; } = new List<ProductSales>();
    }
}
