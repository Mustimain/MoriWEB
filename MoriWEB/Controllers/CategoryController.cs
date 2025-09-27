using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class CategoryController : Controller
    {
        private readonly MoriDbContext _db;
        public CategoryController(MoriDbContext db) => _db = db;

        // Sayfa
        public IActionResult Categories() => View();

        // --- API ---

        // Listele: /Category/List?type=Brand&q=abc
        [HttpGet]
        public async Task<IActionResult> List(LookupType type, string? q)
        {
            q = (q ?? "").Trim().ToLower();
            var data = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == type &&
                           (q == "" || x.Code.ToLower().Contains(q) || x.Name.ToLower().Contains(q)))
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync();

            return Json(data);
        }

        // Kaydet (Create): body = { LookupType, Code, Name }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Lookup model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name))
                return BadRequest("Kod ve Ad zorunludur.");

            var type = model.LookupType;
            var code = model.Code.Trim();
            var name = model.Name.Trim();

            var exists = await _db.Lookups.AnyAsync(x => x.LookupType == type && x.Code == code);
            if (exists) return Conflict(new { message = "Bu kod zaten kayıtlı." });

            _db.Lookups.Add(new Lookup
            {
                LookupType = type,
                Code = code,
                Name = name
            });
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // Güncelle (Update): body = { Id, LookupType, Code, Name }
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] Lookup model)
        {
            if (model == null || model.Id <= 0)
                return BadRequest("Geçersiz Id.");

            if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name))
                return BadRequest("Kod ve Ad zorunludur.");

            var row = await _db.Lookups.FirstOrDefaultAsync(x => x.Id == model.Id);
            if (row == null) return NotFound();

            // Tür değişmiş gelirse engelle (tek tablo bütünlüğü)
            if (row.LookupType != model.LookupType)
                return BadRequest("Tür uyuşmuyor.");

            var code = model.Code.Trim();
            var name = model.Name.Trim();

            var codeTaken = await _db.Lookups.AnyAsync(x =>
                x.LookupType == row.LookupType && x.Code == code && x.Id != row.Id);
            if (codeTaken) return Conflict(new { message = "Bu kod başka bir kayıtta kullanılıyor." });

            row.Code = code;
            row.Name = name;
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }

        // Sil (Delete): body = { Id, LookupType }
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] Lookup model)
        {
            if (model == null || model.Id <= 0)
                return BadRequest("Geçersiz Id.");

            var row = await _db.Lookups.FirstOrDefaultAsync(x => x.Id == model.Id);
            if (row == null) return NotFound();
            if (row.LookupType != model.LookupType)
                return BadRequest("Tür uyuşmuyor.");

            try
            {
                _db.Lookups.Remove(row);
                await _db.SaveChangesAsync();
                return Ok(new { ok = true });
            }
            catch (DbUpdateException)
            {
                return Conflict(new { message = "Bu kayıt ilişkili olduğu için silinemedi." });
            }
        }
    }
}
