using System.ComponentModel.DataAnnotations;

namespace MoriWEB.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string UserName { get; set; } = "";

        // Parola hash
        [Required, MaxLength(500)]
        public string PasswordHash { get; set; } = "";

        // Aktif token (JWT string) — null veya boş ise kullanıcıda token yok
        public string? Token { get; set; }

        // Token'ın DB'de saklanan süresi
        public DateTime? TokenExpiry { get; set; }

        // Kayıt tarihi vb. istersen ekle
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
    }
}
