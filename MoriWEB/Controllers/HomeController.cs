using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using System.Linq;

namespace MoriWEB.Controllers
{
    public class HomeController : Controller
    {
        private readonly MoriDbContext _db;
        public HomeController(MoriDbContext db) => _db = db;

        [HttpGet]
        public IActionResult Index() => View();

        // SOL LÝSTE: Ürünler (+ arama) + stok özeti (Toplam Giriþ - Toplam Satýþ)
        [HttpGet]
        public async Task<IActionResult> Products(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            // Giriþ toplamlarý (ProductEntry üzerinden)
            var entryAgg = await _db.ProductEntries
                .GroupBy(e => e.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalEntry = g.Sum(x => x.Amount ?? 0),
                    LastEntry = g.Max(x => (DateTime?)x.CreateDate)
                })
                .ToListAsync();

            // Satýþ toplamlarý (ProductSales -> ProductEntry -> ProductId)
            var saleAgg = await _db.ProductSales
                .Where(s => s.ProductEntryId != null)
                .Include(s => s.ProductEntry!)
                .GroupBy(s => s.ProductEntry!.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalSales = g.Sum(x => x.Amount ?? 0),
                    LastSale = g.Max(x => (DateTime?)x.CreateDate)
                })
                .ToListAsync();

            var eDict = entryAgg.ToDictionary(x => x.ProductId, x => x);
            var sDict = saleAgg.ToDictionary(x => x.ProductId, x => x);

            var list = await _db.Products.AsNoTracking()
                .Where(p => q == "" ||
                            (p.Code ?? "").ToLower().Contains(q) ||
                            (p.Name ?? "").ToLower().Contains(q))
                .OrderBy(p => p.Code)
                .Select(p => new
                {
                    productId = p.Id,
                    code = p.Code,
                    name = p.Name
                })
                .ToListAsync();

            var result = list.Select(p =>
            {
                eDict.TryGetValue(p.productId, out var e);
                sDict.TryGetValue(p.productId, out var s);
                var totalE = e?.TotalEntry ?? 0;
                var totalS = s?.TotalSales ?? 0;
                return new
                {
                    p.productId,
                    p.code,
                    p.name,
                    inStock = totalE - totalS,
                    lastEntryDate = e?.LastEntry,
                    lastSaleDate = s?.LastSale
                };
            })
            // Stoðu olanlar öne
            .OrderByDescending(x => x.inStock > 0)
            .ThenBy(x => x.code)
            .ToList();

            return Json(result);
        }

        // SAÐ PANEL: Seçilen ürüne ait alýþ ve satýþ kayýtlarý
        [HttpGet]
        public async Task<IActionResult> ProductDetails(int productId)
        {
            var product = await _db.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.Color)
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null) return NotFound("Ürün bulunamadý.");

            // Alýþlar
            var entries = await _db.ProductEntries.AsNoTracking()
                .Include(e => e.Company)
                .Where(e => e.ProductId == productId)
                .OrderByDescending(e => e.CreateDate)
                .Select(e => new
                {
                    id = e.Id,
                    companyName = e.Company != null ? e.Company.Name : null,
                    amount = e.Amount,
                    purchasePrice = e.PurchasePrice,
                    salesPrice = e.SalesPrice,
                    createDate = e.CreateDate
                })
                .ToListAsync();

            // Satýþlar
            var sales = await _db.ProductSales.AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.PaymentType)
                .Include(s => s.ProductEntry)!.ThenInclude(pe => pe.Product)
                .Where(s => s.ProductEntry != null && s.ProductEntry.ProductId == productId)
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    id = s.Id,
                    customerName = s.Customer != null ? ((s.Customer.FirstName + " " + s.Customer.LastName).Trim()) : null,
                    amount = s.Amount,
                    salesPrice = s.SalesPrice,
                    netPrice = s.NetPrice,
                    totalPrice = s.TotalPrice,
                    paymentTypeName = s.PaymentType != null ? s.PaymentType.Name : null,
                    createDate = s.CreateDate
                })
                .ToListAsync();

            var totalEntry = entries.Sum(x => x.amount ?? 0);
            var totalSales = sales.Sum(x => x.amount ?? 0);

            var payload = new
            {
                header = new
                {
                    productId = product.Id,
                    code = product.Code,
                    name = product.Name,
                    brandName = product.Brand?.Name,
                    categoryName = product.Category?.Name,
                    colorName = product.Color?.Name
                },
                entries,
                sales,
                totalEntryAmount = totalEntry,
                totalSalesAmount = totalSales,
                inStock = totalEntry - totalSales
            };

            return Json(payload);
        }
    }
}
