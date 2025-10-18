// Program.cs
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// DbContext (MySQL)
var cs = builder.Configuration.GetConnectionString("DefaultConnection")
         ?? throw new Exception("ConnectionStrings:DefaultConnection bulunamadı!");
builder.Services.AddDbContext<MoriDbContext>(opt =>
{
    var ver = ServerVersion.AutoDetect(cs);
    opt.UseMySql(cs, ver, my => my.EnableRetryOnFailure());
});

// JWT Ayarları (appsettings.json -> "Jwt": {"Key": "...", "Issuer": "...", "Audience": "..."})
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new Exception("Jwt:Key yok!");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MoriWEB";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MoriWEB.Client";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

// AuthN/AuthZ
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = false; // prod'da true yapabilirsiniz
        o.SaveToken = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Pipeline
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

//// Migrate + İlk Kullanıcı Seed
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<MoriDbContext>();
//    db.Database.Migrate();

//    // İlk kullanıcı yoksa ekle
//    if (!db.Users.Any())
//    {
//        var admin = new User
//        {
//            UserName = "admin",
//            PasswordHash = Sha256("memo123*."), // Parolayı buradan değiştirin
//            CreateDate = DateTime.UtcNow
//        };
//        db.Users.Add(admin);
//        db.SaveChanges();
//    }
//}

// Varsayılan route: Login sayfası
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Login}/{id?}");

app.Run();

// --- Helpers ---
static string Sha256(string s)
{
    using var sha = SHA256.Create();
    return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)))
           .Replace("-", "").ToLowerInvariant();
}
