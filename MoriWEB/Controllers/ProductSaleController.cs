using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;
using System.Linq;

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
            // 1) Müşteriler (Customers)
            var customers = await _db.Customers.AsNoTracking()
                .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
                .Select(x => new {
                    id = x.Id,
                    name = ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim(),
                    phone = x.PhoneNumber
                })
                .ToListAsync();

            // 2) Stok girişleri (ProductEntries) + ürün ve firma bilgileri
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
                    salesPrice = e.SalesPrice
                })
                .ToListAsync();

            // 3) Ödeme türleri (Lookups tablosundan PaymentType)
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
                        (((s.Customer.FirstName ?? "").ToLower() + " " + (s.Customer.LastName ?? "").ToLower()).Contains(q) ||
                         (s.Customer.PhoneNumber ?? "").ToLower().Contains(q))) ||
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
                    ProductCode = s.ProductEntry != null && s.ProductEntry.Product != null ? s.ProductEntry.Product.Code : null,
                    ProductName = s.ProductEntry != null && s.ProductEntry.Product != null ? s.ProductEntry.Product.Name : null,
                    CompanyName = s.ProductEntry != null && s.ProductEntry.Company != null ? s.ProductEntry.Company.Name : null,
                    s.ProductEntryId,
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

            return Json(list);
        }

        // CREATE
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductSales dto)
        {
            if (dto == null) return BadRequest("Geçersiz veri");
            if (dto.ProductEntryId == null || dto.ProductEntryId <= 0) return BadRequest("Stok girişi zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar > 0 olmalı.");

            var entry = await _db.ProductEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.ProductEntryId);
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
            return Ok(new { ok = true, id = entity.Id });
        }

        // UPDATE
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ProductSales dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (dto.ProductEntryId == null || dto.ProductEntryId <= 0) return BadRequest("Stok girişi zorunludur.");
            if ((dto.Amount ?? 0) <= 0) return BadRequest("Miktar > 0 olmalı.");

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
            return Ok(new { ok = true, id = s.Id });
        }

        // DELETE
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] ProductSales dto)
        {
            if (dto == null || dto.Id <= 0) return BadRequest("Geçersiz Id");

            var s = await _db.ProductSales.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (s == null) return NotFound();

            _db.ProductSales.Remove(s);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
    }
}
