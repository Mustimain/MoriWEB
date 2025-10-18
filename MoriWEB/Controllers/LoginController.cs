// MoriWEB/Controllers/LoginController.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

[AllowAnonymous]
public class LoginController : Controller
{
    private readonly MoriDbContext _db;
    private readonly IConfiguration _cfg;

    public LoginController(MoriDbContext db, IConfiguration cfg)
    {
        _db = db; _cfg = cfg;
    }

    [HttpGet]
    public IActionResult Login() => View();

    public sealed class LoginRequest
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> Login([FromBody] LoginRequest dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.UserName) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Kullanıcı adı ve parola zorunludur.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == dto.UserName);
        if (user == null) return Unauthorized("Kullanıcı bulunamadı.");

        var incomingHash = Sha256(dto.Password);
        if (!string.Equals(incomingHash, user.PasswordHash, StringComparison.OrdinalIgnoreCase))
            return Unauthorized("Parola hatalı.");

        // Token halen geçerliyse onu kullan (opsiyonel)
        if (!string.IsNullOrWhiteSpace(user.Token) &&
            user.TokenExpiry.HasValue &&
            user.TokenExpiry.Value > DateTime.UtcNow)
        {
            return Json(new { ok = true, token = user.Token, exp = user.TokenExpiry });
        }

        // 30 günlük yeni token
        var (tokenString, expiresUtc) = CreateJwtToken(user);
        user.Token = tokenString;
        user.TokenExpiry = expiresUtc;
        await _db.SaveChangesAsync();

        return Json(new { ok = true, token = tokenString, exp = expiresUtc });
    }

    // Token gerekerek "yaşıyorum" kontrolü
    [Authorize]
    [HttpGet]
    public IActionResult Ping() => Ok(new { ok = true });

    private (string tokenString, DateTime expiresUtc) CreateJwtToken(User user)
    {
        var key = _cfg["Jwt:Key"]!;
        var issuer = _cfg["Jwt:Issuer"] ?? "MoriWEB";
        var audience = _cfg["Jwt:Audience"] ?? "MoriWEB.Client";

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
            new Claim("uid", user.Id.ToString())
        };

        var expires = DateTime.UtcNow.AddDays(30);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static string Sha256(string s)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)))
               .Replace("-", "").ToLowerInvariant();
    }
}
