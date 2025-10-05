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

        // ---------------------- LIST ----------------------
        [HttpGet]
        public async Task<IActionResult> List(LookupType type, string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var query = _db.Lookups.AsNoTracking().Where(x => x.LookupType == type);

            if (!string.IsNullOrEmpty(q))
                query = query.Where(x => x.Code.ToLower().Contains(q) || x.Name.ToLower().Contains(q));

            if (type == LookupType.CashTransactionType)
            {
                var rows = await query
                    .OrderBy(x => x.Name)
                    .Select(x => new
                    {
                        x.Id,
                        x.Code,
                        x.Name,
                        transactionSign = x.TransactionSign // 0: Giriş(−), 1: Çıkış(+)
                    })
                    .ToListAsync();
                return Json(rows);
            }
            else
            {
                var rows = await query
                    .OrderBy(x => x.Name)
                    .Select(x => new { x.Id, x.Code, x.Name })
                    .ToListAsync();
                return Json(rows);
            }
        }

        // ---------------------- CREATE ----------------------
        // DTO kullanmadan, sadece gerekli alanları bind et.
        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody, Bind("LookupType,Code,Name,TransactionSign")] Lookup model)
        {
            if (model == null) return BadRequest("Geçersiz veri.");

            model.Code = (model.Code ?? "").Trim();
            model.Name = (model.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name))
                return BadRequest("Kod ve Ad zorunludur.");

            bool exists = await _db.Lookups.AnyAsync(x => x.LookupType == model.LookupType && x.Code == model.Code);
            if (exists) return Conflict(new { message = "Bu kod zaten kayıtlı." });

            // Sadece CashTransactionType için TransactionSign anlamlıdır (0/1). Diğerlerinde 0'a sabitliyoruz.
            int sign = 0;
            if (model.LookupType == LookupType.CashTransactionType)
            {
                sign = (model.TransactionSign == 1) ? 1 : 0; // 1 veya 0 dışında gelirse 0 kabul et
            }

            var row = new Lookup
            {
                LookupType = model.LookupType,
                Code = model.Code,
                Name = model.Name,
                TransactionSign = sign
            };

            _db.Lookups.Add(row);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // ---------------------- UPDATE ----------------------
        [HttpPost]
        public async Task<IActionResult> Update(
            [FromBody, Bind("Id,LookupType,Code,Name,TransactionSign")] Lookup model)
        {
            if (model == null || model.Id <= 0) return BadRequest("Geçersiz Id.");

            model.Code = (model.Code ?? "").Trim();
            model.Name = (model.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name))
                return BadRequest("Kod ve Ad zorunludur.");

            var row = await _db.Lookups.FirstOrDefaultAsync(x => x.Id == model.Id);
            if (row == null) return NotFound();

            if (row.LookupType != model.LookupType)
                return BadRequest("Tür uyuşmuyor.");

            bool codeTaken = await _db.Lookups.AnyAsync(x =>
                x.LookupType == row.LookupType && x.Code == model.Code && x.Id != row.Id);
            if (codeTaken) return Conflict(new { message = "Bu kod başka bir kayıtta kullanılıyor." });

            row.Code = model.Code;
            row.Name = model.Name;

            if (row.LookupType == LookupType.CashTransactionType)
            {
                // 0/1 harici değer gelirse mevcut değeri korumak yerine 0'a düş.
                row.TransactionSign = (model.TransactionSign == 1) ? 1 : 0;
            }

            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // ---------------------- DELETE ----------------------
        [HttpPost]
        public async Task<IActionResult> Delete(
            [FromBody, Bind("Id,LookupType")] Lookup model)
        {
            if (model == null || model.Id <= 0) return BadRequest("Geçersiz Id.");

            try
            {
                var row = await _db.Lookups.FirstOrDefaultAsync(x => x.Id == model.Id);
                if (row == null) return NotFound();
                if (row.LookupType != model.LookupType) return BadRequest("Tür uyuşmuyor.");

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
