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

        // LOOKUPS: Kasa işlem türleri (Lookups) + satış ve stok girişleri
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            // Kasa türleri artık Lookups tablosundan (LookupType = CashTransactionType) geliyor
            var cashTypes = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType)
                .OrderBy(l => l.Name)
                .Select(l => new
                {
                    id = l.Id,
                    code = l.Code,
                    name = l.Name,
                    sign = l.TransactionSign         // 0 = Giriş(−), 1 = Çıkış(+)
                })
                .ToListAsync();

            // Satışlar (FIFO tüketimlerinden ürün başlığı)
            var sales = await _db.ProductSales.AsNoTracking()
                .Include(s => s.Customer)
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    id = s.Id,
                    prodCode = _db.ProductSaleConsumptions
                        .Where(c => c.ProductSalesId == s.Id)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.Product!.Code)
                        .FirstOrDefault(),
                    prodName = _db.ProductSaleConsumptions
                        .Where(c => c.ProductSalesId == s.Id)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.Product!.Name)
                        .FirstOrDefault(),
                    cust = s.Customer != null ? ((s.Customer.FirstName + " " + s.Customer.LastName).Trim()) : null
                })
                .ToListAsync();

            var salesOut = sales.Select(s => new
            {
                id = s.id,
                text = string.Join(" - ", new[] { s.prodCode, s.prodName }.Where(x => !string.IsNullOrWhiteSpace(x)))
                       + (string.IsNullOrWhiteSpace(s.cust) ? "" : $" [{s.cust}]")
            });

            // Stok girişleri (özet)
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

        // LİSTE / ARAMA
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.CashTransactions.AsNoTracking()
                .Include(c => c.CashTransactionType) // Lookup -> TransactionSign için
                .Include(c => c.ProductEntry)!.ThenInclude(e => e.Product)
                .Include(c => c.ProductSales)!.ThenInclude(s => s.Customer)
                .OrderByDescending(c => c.CreateDate)
                .Select(c => new
                {
                    c.Id,

                    // UI için yön: TransactionSign (0=Giriş(−), 1=Çıkış(+))
                    TransactionType = c.CashTransactionType != null ? (int?)c.CashTransactionType.TransactionSign : null,

                    CashTypeName = c.CashTransactionType != null ? c.CashTransactionType.Name : null,
                    c.CashTransactionTypeId,
                    c.ProductSalesId,
                    c.ProductEntryId,

                    // Ürün kod/adı: satışa bağlıysa FIFO tüketimlerinden, değilse entry’den
                    ProductCode = c.ProductSalesId != null
                        ? _db.ProductSaleConsumptions
                            .Where(x => x.ProductSalesId == c.ProductSalesId)
                            .OrderBy(x => x.Id)
                            .Select(x => x.ProductEntry!.Product!.Code)
                            .FirstOrDefault()
                        : (c.ProductEntry != null && c.ProductEntry.Product != null ? c.ProductEntry.Product.Code : null),

                    ProductName = c.ProductSalesId != null
                        ? _db.ProductSaleConsumptions
                            .Where(x => x.ProductSalesId == c.ProductSalesId)
                            .OrderBy(x => x.Id)
                            .Select(x => x.ProductEntry!.Product!.Name)
                            .FirstOrDefault()
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

        // CREATE — kasa kaydı + bağlıysa alış/satış tutarlarını eşitle
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CashTransaction dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if ((dto.Amount ?? 0m) <= 0) return BadRequest("Tutar > 0 olmalı.");
            if (!(dto.CashTransactionTypeId > 0)) return BadRequest("Kasa türü seçiniz.");

            var entity = new CashTransaction
            {
                ProductSalesId = dto.ProductSalesId,
                ProductEntryId = dto.ProductEntryId,
                CashTransactionTypeId = dto.CashTransactionTypeId, // yön, ilişkili Lookup.TransactionSign’dan gelir
                Amount = dto.Amount,
                Description = dto.Description?.Trim(),
                CreateDate = dto.CreateDate == default ? System.DateTime.Now : dto.CreateDate
            };

            _db.CashTransactions.Add(entity);
            await _db.SaveChangesAsync();

            // Bağlı kayıtların toplamını yeni tutara eşitle
            await SyncLinkedRecordsOnAmountChange(entity);

            return Ok(new { ok = true, id = entity.Id });
        }

        // UPDATE — kasa + bağlı alış/satışa yansıt
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] CashTransaction dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if ((dto.Amount ?? 0m) <= 0) return BadRequest("Tutar > 0 olmalı.");
            if (!(dto.CashTransactionTypeId > 0)) return BadRequest("Kasa türü seçiniz.");

            var c = await _db.CashTransactions.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (c == null) return NotFound();

            c.ProductSalesId = dto.ProductSalesId;
            c.ProductEntryId = dto.ProductEntryId;
            c.CashTransactionTypeId = dto.CashTransactionTypeId; // yön yine kasanın türünden gelecektir
            c.Amount = dto.Amount;
            c.Description = dto.Description?.Trim();
            c.CreateDate = dto.CreateDate == default ? c.CreateDate : dto.CreateDate;

            await _db.SaveChangesAsync();

            await SyncLinkedRecordsOnAmountChange(c);

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

        /// <summary>
        /// Kasa tutarı değiştiyse, bağlı ProductEntry / ProductSales üzerine yeni toplamı yansıtır.
        /// Miktar ve iskonto değişmez; birim fiyatlar yeniden hesaplanır.
        /// </summary>
        private async Task SyncLinkedRecordsOnAmountChange(CashTransaction c)
        {
            if (c.Amount == null) return;

            // Stok girişi (alış)
            if (c.ProductEntryId.HasValue && c.ProductEntryId > 0)
            {
                var e = await _db.ProductEntries.FirstOrDefaultAsync(x => x.Id == c.ProductEntryId.Value);
                if (e != null)
                {
                    var qty = (decimal)(e.Amount ?? 0d);
                    var disc = e.PurchaseDiscount ?? 0m;
                    e.NetPrice = c.Amount; // toplam net tutar = kasa
                    if (qty > 0m)
                    {
                        var denom = (1m - (disc / 100m));
                        if (denom <= 0m) denom = 1m;
                        e.PurchasePrice = (e.NetPrice ?? 0m) / qty / denom;
                    }
                    await _db.SaveChangesAsync();
                }
            }

            // Satış
            if (c.ProductSalesId.HasValue && c.ProductSalesId > 0)
            {
                var s = await _db.ProductSales.FirstOrDefaultAsync(x => x.Id == c.ProductSalesId.Value);
                if (s != null)
                {
                    var qty = (decimal)(s.Amount ?? 0d);
                    var disc = s.SalesDiscount ?? 0m;
                    s.TotalPrice = c.Amount; // toplam = kasa
                    if (qty > 0m)
                    {
                        var netUnit = (s.TotalPrice ?? 0m) / qty; // birim net
                        s.NetPrice = netUnit;
                        var denom = (1m - (disc / 100m));
                        if (denom <= 0m) denom = 1m;
                        s.SalesPrice = netUnit / denom; // birim etiket
                    }
                    await _db.SaveChangesAsync();
                }
            }
        }
    }
}
