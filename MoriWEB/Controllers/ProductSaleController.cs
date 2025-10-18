using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class ProductSaleController : Controller
    {
        private readonly MoriDbContext _db;
        public ProductSaleController(MoriDbContext db) { _db = db; }

        [HttpGet]
        public IActionResult ProductSales() => View();

        // LOOKUPS: müşteri, ürün, ödeme tipleri (en son eklenen ilk)
        [HttpGet]
        public async Task<IActionResult> Lookups()
        {
            var customers = await _db.Customers.AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    id = x.Id,
                    name = ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim(),
                    phone = x.PhoneNumber
                })
                .ToListAsync();

            var products = await _db.Products.AsNoTracking()
                .OrderByDescending(p => p.Id)
                .Select(p => new { id = p.Id, code = p.Code, name = p.Name })
                .ToListAsync();

            var paymentTypes = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.PaymentType)
                .OrderByDescending(l => l.Id)
                .Select(l => new { id = l.Id, name = l.Name })
                .ToListAsync();

            return Json(new { customers, products, paymentTypes });
        }

        // Ürüne göre kalan stok ve son etiket fiyat
        [HttpGet]
        public async Task<IActionResult> ProductInfo(int productId)
        {
            var totalRemaining = await _db.ProductEntries.AsNoTracking()
                .Where(pe => pe.ProductId == productId)
                .SumAsync(pe => (double?)pe.RemainingAmount) ?? 0d;

            var lastLabel = await _db.ProductEntries.AsNoTracking()
                .Where(pe => pe.ProductId == productId && pe.SalesPrice != null)
                .OrderByDescending(pe => pe.Id).ThenByDescending(pe => pe.CreateDate)
                .Select(pe => pe.SalesPrice)
                .FirstOrDefaultAsync();

            return Json(new { totalRemaining, lastLabelPrice = lastLabel ?? 0m });
        }

        // Liste / Arama (en son eklenen ilk)
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.ProductSales.AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.PaymentType)
                .OrderByDescending(s => s.Id).ThenByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    s.Id,
                    CustomerName = s.Customer != null ? ((s.Customer.FirstName + " " + s.Customer.LastName).Trim()) : null,
                    CustomerPhone = s.Customer != null ? s.Customer.PhoneNumber : null,

                    ProductId = _db.ProductSaleConsumptions
                        .Where(c => c.ProductSalesId == s.Id)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.ProductId)
                        .FirstOrDefault(),

                    ProductCode = _db.ProductSaleConsumptions
                        .Where(c => c.ProductSalesId == s.Id)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.Product!.Code)
                        .FirstOrDefault(),

                    ProductName = _db.ProductSaleConsumptions
                        .Where(c => c.ProductSalesId == s.Id)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ProductEntry!.Product!.Name)
                        .FirstOrDefault(),

                    s.Amount,
                    s.SalesPrice,
                    s.SalesDiscount,
                    s.NetPrice,
                    s.TotalPrice,
                    s.PaymentTypeId,
                    PaymentTypeName = s.PaymentType != null ? s.PaymentType.Name : null,
                    s.CustomerId,
                    s.CreateDate
                })
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(q))
            {
                list = list.Where(x =>
                    (x.ProductName ?? "").ToLower().Contains(q) ||
                    (x.ProductCode ?? "").ToLower().Contains(q) ||
                    (x.CustomerName ?? "").ToLower().Contains(q) ||
                    (x.CustomerPhone ?? "").ToLower().Contains(q) ||
                    (x.PaymentTypeName ?? "").ToLower().Contains(q) ||
                    (x.TotalPrice?.ToString().ToLower().Contains(q) ?? false)
                ).ToList();
            }

            return Json(list);
        }

        // CashTransactionType Id bulma (Lookups: SATIS veya sign=1)
        private async Task<int?> FindCashTransactionTypeIdForSale()
        {
            var byCode = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType && l.Code == "SATIS")
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
            if (byCode != null) return byCode;

            var bySign = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == LookupType.CashTransactionType && l.TransactionSign == 1)
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
            return bySign;
        }

        // CREATE — FIFO tüketim + kasa (yalnızca Ödenen Tutar kadar)
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductSales dto, int productId, decimal? paymentAmount)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if (productId <= 0) return BadRequest("Ürün zorunludur.");
            if (dto.CustomerId == null || dto.CustomerId <= 0) return BadRequest("Müşteri zorunludur.");
            if (dto.PaymentTypeId == null || dto.PaymentTypeId <= 0) return BadRequest("Ödeme türü zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar > 0 olmalı.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Satış fiyatı > 0 olmalı.");
            if ((dto.SalesDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                // FIFO stok kontrolü
                double required = dto.Amount ?? 0;
                var fifoList = await _db.ProductEntries
                    .Where(pe => pe.ProductId == productId && pe.RemainingAmount > 0)
                    .OrderBy(pe => pe.CreateDate)
                    .ToListAsync();

                double available = fifoList.Sum(x => x.RemainingAmount);
                if (available + 1e-9 < required)
                    return BadRequest($"Yetersiz stok. Mevcut: {available}, İstenen: {required}");

                var unit = dto.SalesPrice ?? 0m;
                var disc = dto.SalesDiscount ?? 0m;
                var netUnit = unit * (1 - (disc / 100m));
                var total = netUnit * (decimal)required;

                var sale = new ProductSales
                {
                    CustomerId = dto.CustomerId,
                    ProductEntryId = null,
                    Amount = dto.Amount,
                    SalesPrice = dto.SalesPrice,
                    SalesDiscount = dto.SalesDiscount,
                    NetPrice = netUnit,
                    TotalPrice = total,
                    PaymentTypeId = dto.PaymentTypeId,
                    CreateDate = dto.CreateDate == default ? DateTime.Now : dto.CreateDate
                };

                _db.ProductSales.Add(sale);
                await _db.SaveChangesAsync();

                // FIFO tüketimler
                double need = required;
                foreach (var pe in fifoList)
                {
                    if (need <= 1e-9) break;
                    var take = Math.Min(pe.RemainingAmount, need);
                    pe.RemainingAmount -= take;
                    need -= take;

                    _db.ProductSaleConsumptions.Add(new ProductSaleConsumption
                    {
                        ProductSalesId = sale.Id,
                        ProductEntryId = pe.Id,
                        Quantity = take,
                        UnitCost = pe.PurchasePrice ?? 0m,
                        CreateDate = sale.CreateDate
                    });
                }
                await _db.SaveChangesAsync();

                // Ödenen Tutar -> Kasa
                var pay = Math.Max(0m, paymentAmount ?? 0m);
                if (pay > total) return BadRequest("Ödenen Tutar satış toplamını geçemez.");

                if (pay > 0)
                {
                    var cashTypeId = await FindCashTransactionTypeIdForSale();
                    if (cashTypeId == null) return BadRequest("Kasa işlem türü (SATIS) tanımlı değil.");

                    _db.CashTransactions.Add(new CashTransaction
                    {
                        ProductSalesId = sale.Id,
                        CashTransactionTypeId = cashTypeId,
                        Amount = pay,
                        Description = $"Satış Peşin Ödemesi (SalesId={sale.Id})",
                        CreateDate = sale.CreateDate
                    });
                    await _db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return Ok(new { ok = true, id = sale.Id }) as IActionResult;
            });
        }

        // UPDATE — FIFO'yu yeniden yaz + peşin ödeme kaydını güncelle/ekle
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ProductSales dto, int productId, decimal? paymentAmount)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (productId <= 0) return BadRequest("Ürün zorunludur.");
            if (dto.CustomerId == null || dto.CustomerId <= 0) return BadRequest("Müşteri zorunludur.");
            if (dto.PaymentTypeId == null || dto.PaymentTypeId <= 0) return BadRequest("Ödeme türü zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar > 0 olmalı.");
            if ((dto.SalesPrice ?? 0) <= 0) return BadRequest("Satış fiyatı > 0 olmalı.");
            if ((dto.SalesDiscount ?? 0) < 0) return BadRequest("İskonto 0'dan küçük olamaz.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                var sale = await _db.ProductSales.FirstOrDefaultAsync(x => x.Id == dto.Id);
                if (sale == null) return NotFound();

                // Eski tüketimleri iade
                var oldCons = await _db.ProductSaleConsumptions
                    .Where(c => c.ProductSalesId == sale.Id)
                    .Include(c => c.ProductEntry)
                    .ToListAsync();

                foreach (var c in oldCons)
                    if (c.ProductEntry != null) c.ProductEntry.RemainingAmount += c.Quantity;

                _db.ProductSaleConsumptions.RemoveRange(oldCons);
                await _db.SaveChangesAsync();

                // Yeni FIFO
                double required = dto.Amount ?? 0;
                var fifoList = await _db.ProductEntries
                    .Where(pe => pe.ProductId == productId && pe.RemainingAmount > 0)
                    .OrderBy(pe => pe.CreateDate)
                    .ToListAsync();

                double available = fifoList.Sum(x => x.RemainingAmount);
                if (available + 1e-9 < required)
                    return BadRequest($"Yetersiz stok. Mevcut: {available}, İstenen: {required}");

                var unit = dto.SalesPrice ?? 0m;
                var disc = dto.SalesDiscount ?? 0m;
                sale.CustomerId = dto.CustomerId;
                sale.ProductEntryId = null;
                sale.Amount = dto.Amount;
                sale.SalesPrice = dto.SalesPrice;
                sale.SalesDiscount = dto.SalesDiscount;
                sale.NetPrice = unit * (1 - (disc / 100m));
                sale.TotalPrice = sale.NetPrice * (decimal)required;
                sale.PaymentTypeId = dto.PaymentTypeId;
                sale.CreateDate = dto.CreateDate == default ? sale.CreateDate : dto.CreateDate;

                await _db.SaveChangesAsync();

                double need = required;
                foreach (var pe in fifoList)
                {
                    if (need <= 1e-9) break;
                    var take = Math.Min(pe.RemainingAmount, need);
                    pe.RemainingAmount -= take;
                    need -= take;

                    _db.ProductSaleConsumptions.Add(new ProductSaleConsumption
                    {
                        ProductSalesId = sale.Id,
                        ProductEntryId = pe.Id,
                        Quantity = take,
                        UnitCost = pe.PurchasePrice ?? 0m,
                        CreateDate = sale.CreateDate
                    });
                }
                await _db.SaveChangesAsync();

                // Ödenen Tutar (peşin ödeme kaydını güncelle/ekle)
                var pay = Math.Max(0m, paymentAmount ?? 0m);
                if (pay > sale.TotalPrice) return BadRequest("Ödenen Tutar satış toplamını geçemez.");

                var cash = await _db.CashTransactions
                    .Where(c => c.ProductSalesId == sale.Id)
                    .OrderBy(c => c.Id)
                    .FirstOrDefaultAsync();

                if (cash == null)
                {
                    if (pay > 0)
                    {
                        var ctId = await FindCashTransactionTypeIdForSale();
                        if (ctId == null) return BadRequest("Kasa işlem türü (SATIS) tanımlı değil.");
                        _db.CashTransactions.Add(new CashTransaction
                        {
                            ProductSalesId = sale.Id,
                            CashTransactionTypeId = ctId,
                            Amount = pay,
                            Description = $"Satış Peşin Ödemesi (SalesId={sale.Id})",
                            CreateDate = sale.CreateDate
                        });
                    }
                }
                else
                {
                    cash.Amount = pay; // 0 girilirse 0'a çekilir
                    cash.Description = $"Satış (Güncelleme: Peşin Ödeme) SalesId={sale.Id}";
                    cash.CreateDate = sale.CreateDate;

                    if (!(cash.CashTransactionTypeId > 0))
                    {
                        var ctId = await FindCashTransactionTypeIdForSale();
                        if (ctId == null) return BadRequest("Kasa işlem türü (SATIS) tanımlı değil.");
                        cash.CashTransactionTypeId = ctId;
                    }
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return Ok(new { ok = true, id = sale.Id }) as IActionResult;
            });
        }

        // DELETE — stok iade + kasa sil
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] ProductSales dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                var sale = await _db.ProductSales.FirstOrDefaultAsync(x => x.Id == dto.Id);
                if (sale == null) return NotFound();

                var cons = await _db.ProductSaleConsumptions
                    .Where(c => c.ProductSalesId == sale.Id)
                    .Include(c => c.ProductEntry)
                    .ToListAsync();

                foreach (var c in cons)
                    if (c.ProductEntry != null) c.ProductEntry.RemainingAmount += c.Quantity;

                _db.ProductSaleConsumptions.RemoveRange(cons);
                await _db.SaveChangesAsync();

                var cash = await _db.CashTransactions.Where(c => c.ProductSalesId == sale.Id).ToListAsync();
                if (cash.Count > 0) _db.CashTransactions.RemoveRange(cash);
                await _db.SaveChangesAsync();

                _db.ProductSales.Remove(sale);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                return Ok(new { ok = true }) as IActionResult;
            });
        }
    }
}
