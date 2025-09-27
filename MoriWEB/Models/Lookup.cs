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

    }
}
