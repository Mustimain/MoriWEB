using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;

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

            // Alýþ toplamlarý (ProductEntries)
            var entryAgg = await _db.ProductEntries
                .GroupBy(e => e.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalEntry = g.Sum(x => (double?)(x.Amount ?? 0d)) ?? 0d,
                    LastEntry = g.Max(x => (DateTime?)x.CreateDate)
                })
                .ToListAsync();

            // Satýþ toplamlarý artýk FIFO tüketimlerinden (ProductSaleConsumptions)
            var saleAgg = await _db.ProductSaleConsumptions
                .Where(c => c.ProductEntry != null)
                .Select(c => new
                {
                    ProductId = c.ProductEntry!.ProductId,
                    Qty = (double?)(c.Quantity) ?? 0d,
                    Date = (DateTime?)c.CreateDate
                })
                .GroupBy(x => x.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalSales = g.Sum(x => x.Qty),
                    LastSale = g.Max(x => x.Date)
                })
                .ToListAsync();

            var eDict = entryAgg.ToDictionary(x => x.ProductId, x => x);
            var sDict = saleAgg.ToDictionary(x => x.ProductId, x => x);

            var products = await _db.Products.AsNoTracking()
                .Where(p => q == "" ||
                            (p.Code ?? "").ToLower().Contains(q) ||
                            (p.Name ?? "").ToLower().Contains(q))
                .OrderBy(p => p.Code)
                .Select(p => new { p.Id, p.Code, p.Name })
                .ToListAsync();

            var result = products
                .Select(p =>
                {
                    eDict.TryGetValue(p.Id, out var e);
                    sDict.TryGetValue(p.Id, out var s);
                    var totalE = e?.TotalEntry ?? 0d;
                    var totalS = s?.TotalSales ?? 0d;
                    return new
                    {
                        productId = p.Id,
                        code = p.Code,
                        name = p.Name,
                        inStock = totalE - totalS,
                        lastEntryDate = e?.LastEntry,
                        lastSaleDate = s?.LastSale
                    };
                })
                .OrderByDescending(x => x.inStock > 0) // stoðu olanlar öne
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

            // Alýþ listesi
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

            // Satýþ listesi: ürünle iliþkili satýþlarý, tüketim kaydýna göre bul
            var sales = await _db.ProductSales.AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.PaymentType)
                .Where(s =>
                    _db.ProductSaleConsumptions.Any(c =>
                        c.ProductSalesId == s.Id &&
                        c.ProductEntry!.ProductId == productId))
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new
                {
                    id = s.Id,
                    customerName = s.Customer != null
                        ? ((s.Customer.FirstName + " " + s.Customer.LastName).Trim())
                        : null,
                    amount = s.Amount,
                    salesPrice = s.SalesPrice,
                    netPrice = s.NetPrice,
                    totalPrice = s.TotalPrice,
                    paymentTypeName = s.PaymentType != null ? s.PaymentType.Name : null,
                    createDate = s.CreateDate
                })
                .ToListAsync();

            // Toplamlar: giriþleri ProductEntries, satýþlarý ProductSaleConsumptions'tan topla
            var totalEntry = await _db.ProductEntries.AsNoTracking()
                .Where(pe => pe.ProductId == productId)
                .SumAsync(pe => (double?)(pe.Amount ?? 0d)) ?? 0d;

            var totalSales = await _db.ProductSaleConsumptions.AsNoTracking()
                .Where(c => c.ProductEntry!.ProductId == productId)
                .SumAsync(c => (double?)(c.Quantity)) ?? 0d;

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
