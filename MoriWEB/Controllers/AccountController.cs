using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class AccountController : Controller
    {
        private readonly MoriDbContext _db;
        public AccountController(MoriDbContext db) { _db = db; }

        // SAYFA
        [HttpGet]
        public IActionResult MyAccount() => View();

        // LOOKUPS: Kasa işlem türleri + (opsiyonel) satış ve stok girişleri
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            // Kasa işlem türleri (Lookups tablosu)
            var cashTypes = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType)
                .OrderBy(l => l.Name)
                .Select(l => new { id = l.Id, code = l.Code, name = l.Name })
                .ToListAsync();

            // Satışlar (özet)
            var sales = await _db.ProductSales.AsNoTracking()
                .Include(s => s.ProductEntry)!.ThenInclude(e => e.Product)
                .Include(s => s.Customer)
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    id = s.Id,
                    text = (s.ProductEntry != null && s.ProductEntry.Product != null
                                ? (s.ProductEntry.Product.Code + " - " + s.ProductEntry.Product.Name)
                                : $"#{s.Id}") +
                           (s.Customer != null ? $" [{(s.Customer.FirstName + " " + s.Customer.LastName).Trim()}]" : "")
                })
                .ToListAsync();

            // Stok girişleri (özet)
            var entries = await _db.ProductEntries.AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.Company)
                .OrderByDescending(e => e.CreateDate)
                .Select(e => new
                {
                    id = e.Id,
                    text = (e.Product != null ? (e.Product.Code + " - " + e.Product.Name) : $"#{e.Id}") +
                           (e.Company != null ? $" [{e.Company.Name}]" : "")
                })
                .ToListAsync();

            return Json(new { cashTypes, sales, entries });
        }

        // LİSTE / ARAMA
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.CashTransactions.AsNoTracking()
                .Include(c => c.CashTransactionType)
                .Include(c => c.ProductSales)!.ThenInclude(s => s.ProductEntry)!.ThenInclude(e => e.Product)
                .Include(c => c.ProductSales)!.ThenInclude(s => s.Customer)
                .Include(c => c.ProductEntry)!.ThenInclude(e => e.Product)
                .OrderByDescending(c => c.CreateDate)
                .Select(c => new
                {
                    c.Id,
                    c.TransactionType, // 0=Giriş, 1=Çıkış (veya sizin tanımınız)
                    CashTypeName = c.CashTransactionType != null ? c.CashTransactionType.Name : null,
                    c.CashTransactionTypeId,
                    c.ProductSalesId,
                    c.ProductEntryId,
                    ProductCode = c.ProductSales != null && c.ProductSales.ProductEntry != null && c.ProductSales.ProductEntry.Product != null
                        ? c.ProductSales.ProductEntry.Product.Code
                        : (c.ProductEntry != null && c.ProductEntry.Product != null ? c.ProductEntry.Product.Code : null),
                    ProductName = c.ProductSales != null && c.ProductSales.ProductEntry != null && c.ProductSales.ProductEntry.Product != null
                        ? c.ProductSales.ProductEntry.Product.Name
                        : (c.ProductEntry != null && c.ProductEntry.Product != null ? c.ProductEntry.Product.Name : null),
                    CustomerName = c.ProductSales != null && c.ProductSales.Customer != null
                        ? ((c.ProductSales.Customer.FirstName + " " + c.ProductSales.Customer.LastName).Trim())
                        : null,
                    c.Amount,
                    c.Description,
                    c.CreateDate
                })
                .ToListAsync();

            if (string.IsNullOrEmpty(q))
                return Json(list);

            var filtered = list.Where(it =>
                    ((it.ProductCode ?? "").ToLower().Contains(q)) ||
                    ((it.ProductName ?? "").ToLower().Contains(q)) ||
                    ((it.CustomerName ?? "").ToLower().Contains(q)) ||
                    ((it.CashTypeName ?? "").ToLower().Contains(q)) ||
                    ((it.Description ?? "").ToLower().Contains(q)) ||
                    ((it.Amount ?? 0m).ToString().ToLower().Contains(q))
                )
                .ToList();

            return Json(filtered);
        }

        // CREATE — dto olarak CashTransaction yakala
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CashTransaction dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if ((dto.Amount ?? 0m) <= 0) return BadRequest("Tutar > 0 olmalı.");

            // TransactionType belirleme:
            // - Eğer dto.TransactionType gelmişse onu kullan
            // - Gelmediyse ve CashTransactionTypeId 0/1 ise 0=Giriş,1=Çıkış kabul et (isteğinize göre)
            int? txType = dto.TransactionType;
            if (txType == null && dto.CashTransactionTypeId.HasValue && (dto.CashTransactionTypeId == 0 || dto.CashTransactionTypeId == 1))
                txType = dto.CashTransactionTypeId.Value;

            var entity = new CashTransaction
            {
                TransactionType = txType,
                ProductSalesId = dto.ProductSalesId,
                ProductEntryId = dto.ProductEntryId,
                CashTransactionTypeId = dto.CashTransactionTypeId,
                Amount = dto.Amount,
                Description = dto.Description?.Trim(),
                CreateDate = dto.CreateDate == default ? DateTime.Now : dto.CreateDate
            };

            _db.CashTransactions.Add(entity);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true, id = entity.Id });
        }

        // UPDATE
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] CashTransaction dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if ((dto.Amount ?? 0m) <= 0) return BadRequest("Tutar > 0 olmalı.");

            var c = await _db.CashTransactions.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (c == null) return NotFound();

            int? txType = dto.TransactionType;
            if (txType == null && dto.CashTransactionTypeId.HasValue && (dto.CashTransactionTypeId == 0 || dto.CashTransactionTypeId == 1))
                txType = dto.CashTransactionTypeId.Value;

            c.TransactionType = txType;
            c.ProductSalesId = dto.ProductSalesId;
            c.ProductEntryId = dto.ProductEntryId;
            c.CashTransactionTypeId = dto.CashTransactionTypeId;
            c.Amount = dto.Amount;
            c.Description = dto.Description?.Trim();
            c.CreateDate = dto.CreateDate == default ? c.CreateDate : dto.CreateDate;

            await _db.SaveChangesAsync();
            return Ok(new { ok = true, id = c.Id });
        }

        // DELETE
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] CashTransaction dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");

            var c = await _db.CashTransactions.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (c == null) return NotFound();

            _db.CashTransactions.Remove(c);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
    }
}
