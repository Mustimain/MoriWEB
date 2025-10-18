using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MoriWEB.Controllers
{
    public class AccountController : Controller
    {
        private readonly MoriDbContext _db;
        public AccountController(MoriDbContext db) { _db = db; }

        // SAYFA
        [HttpGet]
        public IActionResult MyAccount() => View();

        // LOOKUPS
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            var cashTypes = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType)
                .OrderBy(l => l.Name)
                .Select(l => new
                {
                    id = l.Id,
                    code = l.Code,
                    name = l.Name,
                    sign = l.TransactionSign // 0=Giriş(−), 1=Çıkış(+)
                })
                .ToListAsync();

            var sales = await _db.ProductSales.AsNoTracking()
                .Include(s => s.Customer)
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    id = s.Id,
                    prodCode = _db.ProductSaleConsumptions.Where(c => c.ProductSalesId == s.Id).OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.Product!.Code).FirstOrDefault(),
                    prodName = _db.ProductSaleConsumptions.Where(c => c.ProductSalesId == s.Id).OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.Product!.Name).FirstOrDefault(),
                    cust = s.Customer != null ? ((s.Customer.FirstName + " " + s.Customer.LastName).Trim()) : null
                })
                .ToListAsync();

            var salesOut = sales.Select(s => new
            {
                id = s.id,
                text = string.Join(" - ", new[] { s.prodCode, s.prodName }.Where(x => !string.IsNullOrWhiteSpace(x)))
                       + (string.IsNullOrWhiteSpace(s.cust) ? "" : $" [{s.cust}]")
            });

            var entries = await _db.ProductEntries.AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.Company)
                .OrderByDescending(e => e.CreateDate)
                .Select(e => new
                {
                    id = e.Id,
                    text = (e.Product != null ? (e.Product.Code + " - " + e.Product.Name) : $"#{e.Id}")
                           + (e.Company != null ? $" [{e.Company.Name}]" : "")
                })
                .ToListAsync();

            return Json(new { cashTypes, sales = salesOut, entries });
        }

        // ---- BAKİYE ÖZETLERİ ----
        [HttpGet]
        public async Task<IActionResult> EntryBalance(int id)
        {
            var entry = await _db.ProductEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (entry == null) return NotFound();

            var paid = await _db.CashTransactions.AsNoTracking()
                .Where(c => c.ProductEntryId == id)
                .SumAsync(c => (decimal?)c.Amount) ?? 0m;

            var total = entry.NetPrice ?? 0m;
            var remaining = total - paid;
            if (remaining < 0m) remaining = 0m;

            var payments = await _db.CashTransactions.AsNoTracking()
                .Include(c => c.CashTransactionType)
                .Where(c => c.ProductEntryId == id)
                .OrderByDescending(c => c.CreateDate)
                .Select(c => new
                {
                    c.Id,
                    c.CreateDate,
                    c.Amount,
                    type = c.CashTransactionType != null ? c.CashTransactionType.Name : null,
                    c.Description
                }).ToListAsync();

            return Json(new { total, paid, remaining, payments });
        }

        [HttpGet]
        public async Task<IActionResult> SaleBalance(int id)
        {
            var sale = await _db.ProductSales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (sale == null) return NotFound();

            var paid = await _db.CashTransactions.AsNoTracking()
                .Where(c => c.ProductSalesId == id)
                .SumAsync(c => (decimal?)c.Amount) ?? 0m;

            var total = sale.TotalPrice ?? 0m;
            var remaining = total - paid;
            if (remaining < 0m) remaining = 0m;

            var payments = await _db.CashTransactions.AsNoTracking()
                .Include(c => c.CashTransactionType)
                .Where(c => c.ProductSalesId == id)
                .OrderByDescending(c => c.CreateDate)
                .Select(c => new
                {
                    c.Id,
                    c.CreateDate,
                    c.Amount,
                    type = c.CashTransactionType != null ? c.CashTransactionType.Name : null,
                    c.Description
                }).ToListAsync();

            return Json(new { total, paid, remaining, payments });
        }

        // ---- LİSTE / ARAMA ----
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.CashTransactions.AsNoTracking()
                .Include(c => c.CashTransactionType)
                .Include(c => c.ProductEntry)!.ThenInclude(e => e.Product)
                .Include(c => c.ProductSales)!.ThenInclude(s => s.Customer)
                .OrderByDescending(c => c.CreateDate)
                .Select(c => new
                {
                    c.Id,
                    TransactionType = c.CashTransactionType != null ? (int?)c.CashTransactionType.TransactionSign : null,
                    CashTypeName = c.CashTransactionType != null ? c.CashTransactionType.Name : null,
                    c.CashTransactionTypeId,
                    c.ProductSalesId,
                    c.ProductEntryId,

                    ProductCode = c.ProductSalesId != null
                        ? _db.ProductSaleConsumptions.Where(x => x.ProductSalesId == c.ProductSalesId).OrderBy(x => x.Id)
                            .Select(x => x.ProductEntry!.Product!.Code).FirstOrDefault()
                        : (c.ProductEntry != null && c.ProductEntry.Product != null ? c.ProductEntry.Product.Code : null),

                    ProductName = c.ProductSalesId != null
                        ? _db.ProductSaleConsumptions.Where(x => x.ProductSalesId == c.ProductSalesId).OrderBy(x => x.Id)
                            .Select(x => x.ProductEntry!.Product!.Name).FirstOrDefault()
                        : (c.ProductEntry != null && c.ProductEntry.Product != null ? c.ProductEntry.Product.Name : null),

                    CustomerName = c.ProductSales != null && c.ProductSales.Customer != null
                        ? ((c.ProductSales.Customer.FirstName + " " + c.ProductSales.Customer.LastName).Trim())
                        : null,

                    c.Amount,
                    c.Description,
                    c.CreateDate
                })
                .ToListAsync();

            if (string.IsNullOrEmpty(q)) return Json(list);

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

        // ---- CREATE ----
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CashTransaction dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if ((dto.Amount ?? 0m) <= 0) return BadRequest("Tutar > 0 olmalı.");
            if (!(dto.CashTransactionTypeId > 0)) return BadRequest("İşlem türü seçiniz.");

            // Limit kontrolü (bağlantıya göre)
            if (dto.ProductEntryId.HasValue && dto.ProductEntryId > 0)
            {
                var sum = await _db.CashTransactions.AsNoTracking()
                    .Where(c => c.ProductEntryId == dto.ProductEntryId)
                    .SumAsync(c => (decimal?)c.Amount) ?? 0m;
                var total = await _db.ProductEntries.AsNoTracking()
                    .Where(e => e.Id == dto.ProductEntryId)
                    .Select(e => (decimal?)(e.NetPrice ?? 0m)).FirstOrDefaultAsync() ?? 0m;
                var remaining = total - sum;
                if (dto.Amount > remaining) return BadRequest($"Tutar kalan bakiyeyi aşamaz. Kalan: {remaining:n2}");
            }

            if (dto.ProductSalesId.HasValue && dto.ProductSalesId > 0)
            {
                var sum = await _db.CashTransactions.AsNoTracking()
                    .Where(c => c.ProductSalesId == dto.ProductSalesId)
                    .SumAsync(c => (decimal?)c.Amount) ?? 0m;
                var total = await _db.ProductSales.AsNoTracking()
                    .Where(s => s.Id == dto.ProductSalesId)
                    .Select(s => (decimal?)(s.TotalPrice ?? 0m)).FirstOrDefaultAsync() ?? 0m;
                var remaining = total - sum;
                if (dto.Amount > remaining) return BadRequest($"Tutar kalan bakiyeyi aşamaz. Kalan: {remaining:n2}");
            }

            var entity = new CashTransaction
            {
                ProductSalesId = dto.ProductSalesId,
                ProductEntryId = dto.ProductEntryId,
                CashTransactionTypeId = dto.CashTransactionTypeId,
                Amount = dto.Amount,
                Description = dto.Description?.Trim(),
                CreateDate = dto.CreateDate == default ? System.DateTime.Now : dto.CreateDate
            };

            _db.CashTransactions.Add(entity);
            await _db.SaveChangesAsync();

            // ÖNEMLİ: Satış/alış toplamlarını değiştirmiyoruz (SyncLinkedRecordsOnAmountChange kaldırıldı)
            return Ok(new { ok = true, id = entity.Id });
        }

        // ---- UPDATE ----
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] CashTransaction dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if ((dto.Amount ?? 0m) <= 0) return BadRequest("Tutar > 0 olmalı.");
            if (!(dto.CashTransactionTypeId > 0)) return BadRequest("İşlem türü seçiniz.");

            var c = await _db.CashTransactions.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (c == null) return NotFound();

            // Limit kontrolü: kendi kaydı hariç toplam
            if (dto.ProductEntryId.HasValue && dto.ProductEntryId > 0)
            {
                var sumOthers = await _db.CashTransactions.AsNoTracking()
                    .Where(x => x.ProductEntryId == dto.ProductEntryId && x.Id != c.Id)
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                var total = await _db.ProductEntries.AsNoTracking()
                    .Where(e => e.Id == dto.ProductEntryId)
                    .Select(e => (decimal?)(e.NetPrice ?? 0m)).FirstOrDefaultAsync() ?? 0m;
                var remaining = total - sumOthers;
                if (dto.Amount > remaining) return BadRequest($"Tutar kalan bakiyeyi aşamaz. Kalan: {remaining:n2}");
            }

            if (dto.ProductSalesId.HasValue && dto.ProductSalesId > 0)
            {
                var sumOthers = await _db.CashTransactions.AsNoTracking()
                    .Where(x => x.ProductSalesId == dto.ProductSalesId && x.Id != c.Id)
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;
                var total = await _db.ProductSales.AsNoTracking()
                    .Where(s => s.Id == dto.ProductSalesId)
                    .Select(s => (decimal?)(s.TotalPrice ?? 0m)).FirstOrDefaultAsync() ?? 0m;
                var remaining = total - sumOthers;
                if (dto.Amount > remaining) return BadRequest($"Tutar kalan bakiyeyi aşamaz. Kalan: {remaining:n2}");
            }

            c.ProductSalesId = dto.ProductSalesId;
            c.ProductEntryId = dto.ProductEntryId;
            c.CashTransactionTypeId = dto.CashTransactionTypeId;
            c.Amount = dto.Amount;
            c.Description = dto.Description?.Trim();
            c.CreateDate = dto.CreateDate == default ? c.CreateDate : dto.CreateDate;

            await _db.SaveChangesAsync();

            // ÖNEMLİ: Satış/alış toplamlarını değiştirmiyoruz (SyncLinkedRecordsOnAmountChange kaldırıldı)
            return Ok(new { ok = true, id = c.Id });
        }

        // ---- DELETE ----
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
