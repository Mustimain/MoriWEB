using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class ProductController : Controller
    {
        private readonly MoriDbContext _db;
        public ProductController(MoriDbContext db) { _db = db; }

        // SAYFA
        [HttpGet]
        public IActionResult Products() => View();

        // LOOKUPS
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            var categories = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.Category)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            var brands = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.Brand)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            var fabrics = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.FabricType)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            var models = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.Model)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            var colors = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.Color)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            return Json(new { categories, brands, fabrics, models, colors });
        }

        // LİSTE / ARAMA
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.Products.AsNoTracking()
                .Include(p => p.Category).Include(p => p.Brand).Include(p => p.FabricType)
                .Include(p => p.Model).Include(p => p.Color)
                .Where(p => q == "" ||
                            (p.Name ?? "").ToLower().Contains(q) ||
                            (p.Code ?? "").ToLower().Contains(q) ||
                            (p.Category!.Name ?? "").ToLower().Contains(q) ||
                            (p.Brand!.Name ?? "").ToLower().Contains(q) ||
                            (p.FabricType!.Name ?? "").ToLower().Contains(q) ||
                            (p.Model!.Name ?? "").ToLower().Contains(q) ||
                            (p.Color!.Name ?? "").ToLower().Contains(q))
                .OrderBy(p => p.Name)
                .Select(p => new {
                    p.Id,
                    p.Code,
                    p.Name,
                    p.CreateDate,
                    p.CategoryId,
                    p.BrandId,
                    p.FabricTypeId,
                    p.ModelId,
                    p.ColorId,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    BrandName = p.Brand != null ? p.Brand.Name : null,
                    ColorName = p.Color != null ? p.Color.Name : null
                })
                .ToListAsync();

            return Json(list);
        }

        // OLUŞTUR
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Product dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Ad zorunlu.");
            if (dto.CategoryId <= 0 || dto.BrandId <= 0 || dto.FabricTypeId <= 0 || dto.ModelId <= 0 || dto.ColorId <= 0)
                return BadRequest("Kategori/Marka/Kumaş/Model/Renk zorunlu.");

            var code = await GenerateStockCodeAsync(dto.CategoryId, dto.BrandId, dto.FabricTypeId, dto.ModelId, dto.ColorId);
            if (await _db.Products.AnyAsync(p => p.Code == code))
                return Conflict(new { message = "Aynı stok kodu zaten var." });

            var entity = new Product
            {
                Code = code,
                Name = dto.Name?.Trim(),
                CreateDate = dto.CreateDate == default ? DateTime.Now : dto.CreateDate,
                CategoryId = dto.CategoryId,
                BrandId = dto.BrandId,
                FabricTypeId = dto.FabricTypeId,
                ModelId = dto.ModelId,
                ColorId = dto.ColorId
            };

            _db.Products.Add(entity);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true, id = entity.Id, code = entity.Code });
        }

        // GÜNCELLE
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] Product dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Ad zorunlu.");
            if (dto.CategoryId <= 0 || dto.BrandId <= 0 || dto.FabricTypeId <= 0 || dto.ModelId <= 0 || dto.ColorId <= 0)
                return BadRequest("Kategori/Marka/Kumaş/Model/Renk zorunlu.");

            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (p == null) return NotFound();

            p.Name = dto.Name?.Trim();
            p.CreateDate = dto.CreateDate == default ? p.CreateDate : dto.CreateDate;
            p.CategoryId = dto.CategoryId;
            p.BrandId = dto.BrandId;
            p.FabricTypeId = dto.FabricTypeId;
            p.ModelId = dto.ModelId;
            p.ColorId = dto.ColorId;

            await _db.SaveChangesAsync();
            return Ok(new { ok = true, id = p.Id, code = p.Code });
        }

        // SİL
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] Product dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz id");

            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (p == null) return NotFound();

            _db.Products.Remove(p);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // Yardımcı
        private async Task<string> GenerateStockCodeAsync(int categoryId, int brandId, int fabricId, int modelId, int colorId)
        {
            var cat = await _db.Lookups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == categoryId);
            var brand = await _db.Lookups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == brandId);
            var fab = await _db.Lookups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fabricId);
            var mdl = await _db.Lookups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == modelId);
            var clr = await _db.Lookups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == colorId);

            string c1 = (cat?.Code ?? "CAT").Trim();
            string c2 = (brand?.Code ?? "BRD").Trim();
            string c3 = (fab?.Code ?? "FAB").Trim();
            string c4 = (mdl?.Code ?? "MDL").Trim();
            string c5 = (clr?.Code ?? "CLR").Trim();

            var prefix = $"{c1}{c2}{c3}{c4}-{c5}-";
            var codes = await _db.Products.AsNoTracking()
                .Where(p => p.Code != null && p.Code.StartsWith(prefix))
                .Select(p => p.Code!)
                .ToListAsync();

            int maxSuffix = 0;
            foreach (var code in codes)
            {
                var tail = code.Length > prefix.Length ? code.Substring(prefix.Length) : "";
                if (int.TryParse(tail, out var n) && n > maxSuffix) maxSuffix = n;
            }
            var suffix = (maxSuffix + 1).ToString("D5");
            return prefix + suffix;
        }
    }
}
