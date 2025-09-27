using System.ComponentModel.DataAnnotations;

namespace MoriWEB.Models
{
    public class Product
    {
        public int Id { get; set; }
        [Required,MaxLength(64)] public string? Code { get; set; }   // Stok Kodu
        [MaxLength(256)] public string? Name { get; set; }  // Stok Adı

        [Required] public int CategoryId { get; set; }
        public Lookup? Category { get; set; }

        [Required] public int BrandId { get; set; }
        public Lookup? Brand { get; set; }

        [Required] public int FabricTypeId { get; set; }
        public Lookup? FabricType { get; set; }

        [Required] public int ModelId { get; set; }
        public Lookup? Model { get; set; }

        [Required] public int ColorId { get; set; }
        public Lookup? Color { get; set; }

        public DateTime CreateDate { get; set; } = DateTime.Now;


    }

}
