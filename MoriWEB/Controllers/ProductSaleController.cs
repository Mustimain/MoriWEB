using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MoriWEB.Controllers
{
    public class ProductSaleController : Controller
    {
        private readonly MoriDbContext _db;
        public ProductSaleController(MoriDbContext db) { _db = db; }

        [HttpGet]
        public IActionResult ProductSales() => View();

        // LOOKUPS
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            // 1) Müşteriler
            var customers = await _db.Customers.AsNoTracking()
                .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
                .Select(x => new {
                    id = x.Id,
                    name = ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim(),
                    phone = x.PhoneNumber
                })
                .ToListAsync();

            // 2) Stok girişleri (ürün, firma, etiket fiyat)
            var entries = await _db.ProductEntries.AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.Company)
                .OrderByDescending(e => e.CreateDate)
                .Select(e => new {
                    id = e.Id,
                    productEntryId = e.Id,
                    productId = e.ProductId,
                    productCode = e.Product != null ? e.Product.Code : null,
                    productName = e.Product != null ? e.Product.Name : null,
                    companyId = e.CompanyId,
                    companyName = e.Company != null ? e.Company.Name : null,
                    amount = e.Amount,
                    purchasePrice = e.PurchasePrice,
                    labelPrice = e.SalesPrice  // <- stok girişindeki etiket
                })
                .ToListAsync();

            // 3) Ödeme türleri
            var paymentTypes = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.PaymentType)
                .OrderBy(l => l.Name)
                .Select(l => new { id = l.Id, name = l.Name })
                .ToListAsync();

            return Json(new { customers, entries, paymentTypes });
        }

        // LİSTE / ARAMA
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.ProductSales.AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.PaymentType)
                .Include(s => s.ProductEntry)!.ThenInclude(e => e.Product)
                .Include(s => s.ProductEntry)!.ThenInclude(e => e.Company)
                .Where(s =>
                    q == "" ||
                    ((s.Customer != null &&
                        ((((s.Customer.FirstName ?? "").ToLower() + " " + (s.Customer.LastName ?? "").ToLower()).Contains(q)) ||
                         ((s.Customer.PhoneNumber ?? "").ToLower().Contains(q)))) ||
                     (s.ProductEntry != null && s.ProductEntry.Product != null &&
                        (((s.ProductEntry.Product.Name ?? "").ToLower().Contains(q)) ||
                         ((s.ProductEntry.Product.Code ?? "").ToLower().Contains(q)))) ||
                     (s.ProductEntry != null && s.ProductEntry.Company != null &&
                        ((s.ProductEntry.Company.Name ?? "").ToLower().Contains(q))) ||
                     ((s.TotalPrice ?? 0).ToString().ToLower().Contains(q))
                    )
                )
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    s.Id,
                    CustomerName = s.Customer != null ? ((s.Customer.FirstName + " " + s.Customer.LastName).Trim()) : null,
                    CustomerPhone = s.Customer != null ? s.Customer.PhoneNumber : null,
                    ProductEntryId = s.ProductEntryId,
                    ProductCode = s.ProductEntry != null && s.ProductEntry.Product != null ? s.ProductEntry.Product.Code : null,
                    ProductName = s.ProductEntry != null && s.ProductEntry.Product != null ? s.ProductEntry.Product.Name : null,
                    CompanyName = s.ProductEntry != null && s.ProductEntry.Company != null ? s.ProductEntry.Company.Name : null,
                    s.Amount,
                    s.SalesPrice,
                    s.SalesDiscount,
                    s.NetPrice,
                    s.TotalPrice,
                    s.PaymentTypeId,
                    PaymentTypeName = s.PaymentType != null ? s.PaymentType.Name : null,
                    s.CustomerId,
                    s.CreateDate,
                    LabelPrice = s.ProductEntry != null ? s.ProductEntry.SalesPrice : null
                })
                .ToListAsync();

            return Json(list);
        }

        // --- yardımcı: kasa tipi bul
        private async Task<int?> FindCashTransactionTypeId(string code)
        {
            var ct = await _db.Lookups.AsNoTracking()
                .FirstOrDefaultAsync(l => l.LookupType == LookupType.CashTransactionType && l.Code == code);
            return ct?.Id;
        }

        // CREATE
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductSales dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if (dto.ProductEntryId == null || dto.ProductEntryId <= 0) return BadRequest("Stok girişi zorunludur.");
            if (dto.CustomerId == null || dto.CustomerId <= 0) return BadRequest("Müşteri zorunludur.");
            if (dto.PaymentTypeId == null || dto.PaymentTypeId <= 0) return BadRequest("Ödeme türü zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar > 0 olmalı.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Satış fiyatı > 0 olmalı.");
            if ((dto.SalesDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            var entry = await _db.ProductEntries
                .Include(e => e.Product).Include(e => e.Company)
                .AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.ProductEntryId);
            if (entry == null) return BadRequest("Stok girişi bulunamadı.");

            var unit = dto.SalesPrice ?? 0m;
            var disc = dto.SalesDiscount ?? 0m;
            var netUnit = unit * (1 - (disc / 100m));
            var total = netUnit * (decimal)(dto.Amount ?? 0);

            var entity = new ProductSales
            {
                CustomerId = dto.CustomerId,
                ProductEntryId = dto.ProductEntryId,
                Amount = dto.Amount,
                SalesPrice = dto.SalesPrice,
                SalesDiscount = dto.SalesDiscount,
                NetPrice = netUnit,
                TotalPrice = total,
                PaymentTypeId = dto.PaymentTypeId,
                CreateDate = dto.CreateDate == default ? DateTime.Now : dto.CreateDate
            };

            _db.ProductSales.Add(entity);
            await _db.SaveChangesAsync();

            // Kasa kaydı — SATIŞ (gelir)
            var cashTypeId = await FindCashTransactionTypeId("SATIS");
            var cash = new CashTransaction
            {
                TransactionType = 0, // <- diğerinin tam tersi, senin isteğin
                ProductSalesId = entity.Id,
                CashTransactionTypeId = cashTypeId,
                Amount = total,
                Description = $"Satış: {(entry.Product?.Code)} - {(entry.Product?.Name)} x{dto.Amount}",
                CreateDate = entity.CreateDate
            };
            _db.CashTransactions.Add(cash);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true, id = entity.Id });
        }

        // UPDATE
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ProductSales dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (dto.ProductEntryId == null || dto.ProductEntryId <= 0) return BadRequest("Stok girişi zorunludur.");
            if (dto.CustomerId == null || dto.CustomerId <= 0) return BadRequest("Müşteri zorunludur.");
            if (dto.PaymentTypeId == null || dto.PaymentTypeId <= 0) return BadRequest("Ödeme türü zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar > 0 olmalı.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Satış fiyatı > 0 olmalı.");
            if ((dto.SalesDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            var s = await _db.ProductSales.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (s == null) return NotFound();

            if (!await _db.ProductEntries.AnyAsync(e => e.Id == dto.ProductEntryId))
                return BadRequest("Stok girişi bulunamadı.");

            s.CustomerId = dto.CustomerId;
            s.ProductEntryId = dto.ProductEntryId;
            s.Amount = dto.Amount;
            s.SalesPrice = dto.SalesPrice;
            s.SalesDiscount = dto.SalesDiscount;

            var unit = dto.SalesPrice ?? 0m;
            var disc = dto.SalesDiscount ?? 0m;
            s.NetPrice = unit * (1 - (disc / 100m));
            s.TotalPrice = s.NetPrice * (decimal)(dto.Amount ?? 0);

            s.PaymentTypeId = dto.PaymentTypeId;
            s.CreateDate = dto.CreateDate == default ? s.CreateDate : dto.CreateDate;

            await _db.SaveChangesAsync();

            // Bağlı kasa hareketini güncelle
            var cash = await _db.CashTransactions.FirstOrDefaultAsync(c => c.ProductSalesId == s.Id);
            if (cash != null)
            {
                cash.TransactionType = 0; // satış
                cash.Amount = s.TotalPrice;
                cash.Description = $"Satış (Güncelleme): SalesId={s.Id} x{s.Amount}";
                cash.CreateDate = s.CreateDate;
                await _db.SaveChangesAsync();
            }

            return Ok(new { ok = true, id = s.Id });
        }

        // DELETE
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] ProductSales dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");

            var s = await _db.ProductSales.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (s == null) return NotFound();

            // kasa kayıtlarını da sil
            var cash = await _db.CashTransactions.Where(c => c.ProductSalesId == s.Id).ToListAsync();
            if (cash.Count > 0)
                _db.CashTransactions.RemoveRange(cash);

            _db.ProductSales.Remove(s);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
    }
}
