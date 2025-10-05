using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class ProductEntryController : Controller
    {
        private readonly MoriDbContext _db;
        public ProductEntryController(MoriDbContext db) { _db = db; }

        [HttpGet]
        public IActionResult ProductEntries() => View();

        // Lookups: şirket(Company) ve ürün listesi
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            var companies = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.Company)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            var products = await _db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new { id = p.Id, code = p.Code, name = p.Name })
                .ToListAsync();

            return Json(new { companies, products });
        }

        // Liste / Arama
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.ProductEntries.AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.Company)
                .Where(e =>
                    q == "" ||
                    ((e.Product != null && ((e.Product.Name ?? "").ToLower().Contains(q) || (e.Product.Code ?? "").ToLower().Contains(q))) ||
                     (e.Company != null && ((e.Company.Name ?? "").ToLower().Contains(q) || (e.Company.Code ?? "").ToLower().Contains(q))) ||
                     e.CreateDate.ToString().ToLower().Contains(q)
                    )
                )
                .OrderByDescending(e => e.CreateDate)
                .Select(e => new
                {
                    e.Id,
                    e.ProductId,
                    ProductCode = e.Product != null ? e.Product.Code : null,
                    ProductName = e.Product != null ? e.Product.Name : null,
                    e.CompanyId,
                    CompanyCode = e.Company != null ? e.Company.Code : null,
                    CompanyName = e.Company != null ? e.Company.Name : null,
                    e.Amount,
                    e.PurchasePrice,
                    e.PurchaseDiscount,
                    e.NetPrice,
                    e.SalesPrice,
                    e.CreateDate
                })
                .ToListAsync();

            return Json(list);
        }

        // Create — doğrulamalar + Kasa hareketi
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductEntry dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if (dto.ProductId <= 0 || dto.CompanyId <= 0) return BadRequest("Ürün ve Firma zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar 0'dan büyük olmalıdır.");
            if ((dto.PurchasePrice ?? 0) <= 0) return BadRequest("Alış fiyatı 0'dan büyük olmalıdır.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Etiket fiyatı 0'dan büyük olmalıdır.");
            if ((dto.PurchaseDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductId);
            if (product == null) return BadRequest("Ürün bulunamadı.");
            var company = await _db.Lookups.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == dto.CompanyId && l.LookupType == LookupType.Company);
            if (company == null) return BadRequest("Firma bulunamadı.");

            // NetPrice = Amount * PurchasePrice * (1 - disc/100)
            var amount = (decimal)(dto.Amount ?? 0);
            var price = dto.PurchasePrice ?? 0;
            var disc = dto.PurchaseDiscount ?? 0;
            var net = (amount * price) * (1 - (disc / 100m));

            var entity = new ProductEntry
            {
                ProductId = dto.ProductId,
                CompanyId = dto.CompanyId,
                Amount = dto.Amount,
                PurchasePrice = dto.PurchasePrice,
                PurchaseDiscount = dto.PurchaseDiscount,
                NetPrice = net,
                SalesPrice = dto.SalesPrice,
                CreateDate = dto.CreateDate == default ? DateTime.Now : dto.CreateDate
            };

            _db.ProductEntries.Add(entity);
            await _db.SaveChangesAsync();

            // Kasa hareketi (Çıkış)
            var cashTypeId = await FindCashTransactionTypeId("STOK_GIRIS"); // bulunamazsa null döner
            var cash = new CashTransaction
            {
                TransactionType = 1, // 1=Giriş, 2=Çıkış
                ProductEntryId = entity.Id,
                CashTransactionTypeId = cashTypeId,
                Amount = net,
                Description = $"Stok Girişi",
                CreateDate = entity.CreateDate
            };
            _db.CashTransactions.Add(cash);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true, id = entity.Id });
        }

        // Update — doğrulamalar + Kasa hareketini eşitle
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ProductEntry dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (dto.ProductId <= 0 || dto.CompanyId <= 0) return BadRequest("Ürün ve Firma zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar 0'dan büyük olmalıdır.");
            if ((dto.PurchasePrice ?? 0) <= 0) return BadRequest("Alış fiyatı 0'dan büyük olmalıdır.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Etiket fiyatı 0'dan büyük olmalıdır.");
            if ((dto.PurchaseDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            var e = await _db.ProductEntries.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (e == null) return NotFound();

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId)) return BadRequest("Ürün bulunamadı.");
            if (!await _db.Lookups.AnyAsync(l => l.Id == dto.CompanyId && l.LookupType == LookupType.Company)) return BadRequest("Firma bulunamadı.");

            e.ProductId = dto.ProductId;
            e.CompanyId = dto.CompanyId;
            e.Amount = dto.Amount;
            e.PurchasePrice = dto.PurchasePrice;
            e.PurchaseDiscount = dto.PurchaseDiscount;

            var amount = (decimal)(dto.Amount ?? 0);
            var price = dto.PurchasePrice ?? 0;
            var disc = dto.PurchaseDiscount ?? 0;
            e.NetPrice = (amount * price) * (1 - (disc / 100m));

            e.SalesPrice = dto.SalesPrice;
            e.CreateDate = dto.CreateDate == default ? e.CreateDate : dto.CreateDate;

            await _db.SaveChangesAsync();

            // İlgili kasa hareketini güncelle
            var cash = await _db.CashTransactions.FirstOrDefaultAsync(c => c.ProductEntryId == e.Id);
            if (cash != null)
            {
                cash.Amount = e.NetPrice;
                cash.Description = $"Stok Girişi";
                cash.CreateDate = e.CreateDate;
                cash.TransactionType = 1;
                await _db.SaveChangesAsync();
            }

            return Ok(new { ok = true, id = e.Id });
        }

        // Delete — ilgili kasa hareketini de sil
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] ProductEntry dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");

            var e = await _db.ProductEntries.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (e == null) return NotFound();

            var cash = await _db.CashTransactions.Where(c => c.ProductEntryId == e.Id).ToListAsync();
            if (cash.Count > 0)
            {
                _db.CashTransactions.RemoveRange(cash);
            }

            _db.ProductEntries.Remove(e);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // "STOK_GIRIS" kodlu kasa hareket tipi varsa Id'sini döndürür; yoksa null.
        private async Task<int?> FindCashTransactionTypeId(string code)
        {
            var ct = await _db.Lookups.AsNoTracking()
                .FirstOrDefaultAsync(l => l.LookupType == LookupType.CashTransactionType && l.Code == code);
            return ct?.Id;
        }
    }
}
