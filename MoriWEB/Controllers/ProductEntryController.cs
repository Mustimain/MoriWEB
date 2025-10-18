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

        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            // --- Firmalar: son giriş (alış) tarihine göre ---
            var companies = await _db.Lookups.AsNoTracking()
                .Where(x => x.LookupType == LookupType.Company)
                .Select(c => new
                {
                    c.Id,
                    c.Code,
                    c.Name,
                    LastEntryDate = _db.ProductEntries
                                      .Where(pe => pe.CompanyId == c.Id)
                                      .Max(pe => (DateTime?)pe.CreateDate)
                })
                .OrderByDescending(x => x.LastEntryDate ?? DateTime.MinValue)
                .ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            // --- Ürünler: son giriş (alış) tarihine göre ---
            var products = await _db.Products.AsNoTracking()
                .Select(p => new
                {
                    p.Id,
                    p.Code,
                    p.Name,
                    LastEntryDate = _db.ProductEntries
                                      .Where(pe => pe.ProductId == p.Id)
                                      .Max(pe => (DateTime?)pe.CreateDate)
                })
                .OrderByDescending(x => x.LastEntryDate ?? DateTime.MinValue)
                .ThenBy(x => x.Name)
                .Select(x => new { id = x.Id, code = x.Code, name = x.Name })
                .ToListAsync();

            return Json(new { companies, products });
        }

        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.ProductEntries.AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.Company)
                .Where(e =>
                    q == "" ||
                    ((e.Product != null && (
                        ((e.Product.Name ?? "").ToLower().Contains(q)) ||
                        ((e.Product.Code ?? "").ToLower().Contains(q))
                    )) ||
                    (e.Company != null && (
                        ((e.Company.Name ?? "").ToLower().Contains(q)) ||
                        ((e.Company.Code ?? "").ToLower().Contains(q))
                    )) ||
                    e.CreateDate.ToString().ToLower().Contains(q))
                )
                // --- Sıralama: önce Id DESC, eşitlikte CreateDate DESC (en son eklenen ilk)
                .OrderByDescending(e => e.Id)
                .ThenByDescending(e => e.CreateDate)
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
                    e.RemainingAmount,
                    e.PurchasePrice,
                    e.PurchaseDiscount,
                    e.NetPrice,
                    e.SalesPrice,
                    e.CreateDate
                })
                .ToListAsync();

            return Json(list);
        }

        // --- Yardımcı: Kasa işlem türü Id bul (Lookups üzerinden) ---
        private async Task<int?> FindCashTransactionTypeIdForPurchase()
        {
            var byCode = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType && l.Code == "ALIS")
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
            if (byCode != null) return byCode;

            var bySign = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType && l.TransactionSign == 0)
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();

            return bySign;
        }

        // --- CREATE ---
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductEntry dto, decimal? paymentAmount)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if (dto.ProductId <= 0 || dto.CompanyId <= 0) return BadRequest("Ürün ve Firma zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar 0'dan büyük olmalıdır.");
            if ((dto.PurchasePrice ?? 0) <= 0) return BadRequest("Alış fiyatı 0'dan büyük olmalıdır.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Etiket (satış) fiyatı 0'dan büyük olmalıdır.");
            if ((dto.PurchaseDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest("Ürün bulunamadı.");
            if (!await _db.Lookups.AnyAsync(l => l.Id == dto.CompanyId && l.LookupType == LookupType.Company))
                return BadRequest("Firma bulunamadı.");

            var amountDec = (decimal)(dto.Amount ?? 0d);
            var unit = dto.PurchasePrice ?? 0m;
            var discount = dto.PurchaseDiscount ?? 0m;
            var net = (amountDec * unit) * (1 - (discount / 100m));

            var paid = Math.Max(0m, paymentAmount ?? 0m);
            if (paid > net) return BadRequest("Ödenen Tutar, net toplamı geçemez.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                var entity = new ProductEntry
                {
                    ProductId = dto.ProductId,
                    CompanyId = dto.CompanyId,
                    Amount = dto.Amount,
                    RemainingAmount = dto.Amount ?? 0d,
                    PurchasePrice = unit,
                    PurchaseDiscount = discount,
                    NetPrice = net,
                    SalesPrice = dto.SalesPrice,
                    CreateDate = dto.CreateDate == default ? DateTime.Now : dto.CreateDate
                };

                _db.ProductEntries.Add(entity);
                await _db.SaveChangesAsync();

                if (paid > 0)
                {
                    var cashTypeId = await FindCashTransactionTypeIdForPurchase();
                    if (cashTypeId == null)
                        return BadRequest("Kasa işlem türü (ALIS / TransactionSign=0) bulunamadı.");

                    _db.CashTransactions.Add(new CashTransaction
                    {
                        ProductEntryId = entity.Id,
                        CashTransactionTypeId = cashTypeId,
                        Amount = paid,
                        Description = "Alış / Peşin Ödeme",
                        CreateDate = entity.CreateDate
                    });
                    await _db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return Ok(new { ok = true, id = entity.Id }) as IActionResult;
            });
        }

        // --- UPDATE ---
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ProductEntry dto, decimal? paymentAmount)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (dto.ProductId <= 0 || dto.CompanyId <= 0) return BadRequest("Ürün ve Firma zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar 0'dan büyük olmalıdır.");
            if ((dto.PurchasePrice ?? 0) <= 0) return BadRequest("Alış fiyatı 0'dan büyük olmalıdır.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Etiket (satış) fiyatı 0'dan büyük olmalıdır.");
            if ((dto.PurchaseDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest("Ürün bulunamadı.");
            if (!await _db.Lookups.AnyAsync(l => l.Id == dto.CompanyId && l.LookupType == LookupType.Company))
                return BadRequest("Firma bulunamadı.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                var e = await _db.ProductEntries.FirstOrDefaultAsync(x => x.Id == dto.Id);
                if (e == null) return NotFound();

                var oldAmount = e.Amount ?? 0d;
                var consumed = oldAmount - e.RemainingAmount;   // daha önce satılmış miktar
                var newAmount = dto.Amount ?? 0d;
                var newRemaining = Math.Max(0d, newAmount - consumed);

                e.ProductId = dto.ProductId;
                e.CompanyId = dto.CompanyId;
                e.Amount = newAmount;
                e.RemainingAmount = newRemaining;
                e.PurchasePrice = dto.PurchasePrice;
                e.PurchaseDiscount = dto.PurchaseDiscount;

                var amountDec = (decimal)newAmount;
                var discount = dto.PurchaseDiscount ?? 0m;
                var unit = dto.PurchasePrice ?? 0m;
                e.NetPrice = (amountDec * unit) * (1 - (discount / 100m)); // yeni net toplam

                e.SalesPrice = dto.SalesPrice;
                e.CreateDate = dto.CreateDate == default ? e.CreateDate : dto.CreateDate;

                var paid = Math.Max(0m, paymentAmount ?? 0m);
                if (paid > e.NetPrice) return BadRequest("Ödenen Tutar, net toplamı geçemez.");

                await _db.SaveChangesAsync();

                var cash = await _db.CashTransactions
                    .Where(c => c.ProductEntryId == e.Id)
                    .OrderBy(c => c.Id)
                    .FirstOrDefaultAsync();

                if (cash == null)
                {
                    if (paid > 0)
                    {
                        var cashTypeId = await FindCashTransactionTypeIdForPurchase();
                        if (cashTypeId == null)
                            return BadRequest("Kasa işlem türü (ALIS / TransactionSign=0) bulunamadı.");

                        _db.CashTransactions.Add(new CashTransaction
                        {
                            ProductEntryId = e.Id,
                            CashTransactionTypeId = cashTypeId,
                            Amount = paid,
                            Description = "Alış / Peşin Ödeme",
                            CreateDate = e.CreateDate
                        });
                    }
                }
                else
                {
                    cash.Amount = paid; // 0 girilirse 0'a çekilir
                    cash.Description = "Alış / Peşin Ödeme (Güncelleme)";
                    cash.CreateDate = e.CreateDate;

                    if (!(cash.CashTransactionTypeId > 0))
                    {
                        var ctId = await FindCashTransactionTypeIdForPurchase();
                        if (ctId == null) return BadRequest("Kasa işlem türü (ALIS / TransactionSign=0) bulunamadı.");
                        cash.CashTransactionTypeId = ctId;
                    }
                }

                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                return Ok(new { ok = true, id = e.Id }) as IActionResult;
            });
        }

        // --- DELETE (bağlı tüketim varsa engelle) ---
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] ProductEntry dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                var e = await _db.ProductEntries.FirstOrDefaultAsync(x => x.Id == dto.Id);
                if (e == null) return NotFound();

                var hasConsumption = await _db.ProductSaleConsumptions.AnyAsync(c => c.ProductEntryId == e.Id);
                if (hasConsumption) return Conflict("Bu girişe bağlı satış tüketimleri var. Silinemez.");

                var cash = await _db.CashTransactions.Where(c => c.ProductEntryId == e.Id).ToListAsync();
                if (cash.Count > 0) _db.CashTransactions.RemoveRange(cash);

                _db.ProductEntries.Remove(e);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                return Ok(new { ok = true }) as IActionResult;
            });
        }
    }
}
