using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class CreditTrackingController : Controller
    {
        private readonly MoriDbContext _db;
        public CreditTrackingController(MoriDbContext db)
        {
            _db = db;
        }

        public IActionResult CreditTrackings() => View();

        [HttpGet]
        public async Task<IActionResult> Customers(string? q = null)
        {
            q = (q ?? "").Trim().ToLower();

            var sales = await _db.ProductSales
                .AsNoTracking()
                .Where(s => s.CustomerId != null)
                .Select(s => new
                {
                    s.Id,
                    CustomerId = s.CustomerId!.Value,
                    Total = (decimal)(s.TotalPrice ?? 0m)
                })
                .ToListAsync();

            var saleIds = sales.Select(s => s.Id).ToList();

            var paymentsBySale = await _db.CashTransactions
                .AsNoTracking()
                .Where(t => t.ProductSalesId != null && saleIds.Contains(t.ProductSalesId.Value))
                .Where(t => t.CashTransactionType != null && t.CashTransactionType.TransactionSign == 1)
                .GroupBy(t => t.ProductSalesId!.Value)
                .Select(g => new { SaleId = g.Key, Paid = g.Sum(x => (decimal)(x.Amount ?? 0m)) })
                .ToListAsync();

            var paidLookup = paymentsBySale.ToDictionary(x => x.SaleId, x => x.Paid);

            var saleRemaining = sales.Select(s =>
            {
                var paid = paidLookup.TryGetValue(s.Id, out var p) ? p : 0m;
                var remain = s.Total - paid;
                if (remain < 0) remain = 0;
                return new { s.CustomerId, s.Total, Paid = paid, Remain = remain };
            }).ToList();

            var agg = saleRemaining
                .GroupBy(x => x.CustomerId)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    TotalSales = g.Sum(z => z.Total),
                    TotalPaid = g.Sum(z => z.Paid),
                    Remaining = g.Sum(z => z.Remain)
                })
                .ToList();

            var customers = await _db.Customers.AsNoTracking().ToListAsync();

            var rows = customers.Select(c =>
            {
                var a = agg.FirstOrDefault(x => x.CustomerId == c.Id);
                var totalSales = a?.TotalSales ?? 0m;
                var totalPaid = a?.TotalPaid ?? 0m;
                var remaining = a?.Remaining ?? 0m;

                var name = $"{c.FirstName ?? ""} {c.LastName ?? ""}".Trim();
                return new
                {
                    id = c.Id,
                    name = string.IsNullOrWhiteSpace(name) ? "(İsimsiz)" : name,
                    phone = c.PhoneNumber,
                    email = c.EmailAdress,
                    totalSales,
                    totalPaid,
                    remaining
                };
            });

            if (string.IsNullOrEmpty(q))
            {
                rows = rows.Where(x => x.remaining > 0m);
            }
            else
            {
                rows = rows.Where(x =>
                    (x.name ?? "").ToLower().Contains(q) ||
                    (x.phone ?? "").ToLower().Contains(q) ||
                    (x.email ?? "").ToLower().Contains(q));
            }

            var result = rows
                .OrderByDescending(x => x.remaining)
                .ThenBy(x => x.name)
                .ToList();

            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> CustomerDetails(int customerId)
        {
            var cust = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId);
            if (cust == null) return NotFound("Müşteri bulunamadı.");

            var sales = await _db.ProductSales
                .AsNoTracking()
                .Where(s => s.CustomerId == customerId)
                .Select(s => new
                {
                    s.Id,
                    s.CreateDate,
                    s.Amount,
                    s.SalesPrice,
                    s.NetPrice,
                    s.TotalPrice,
                    ProductCode = s.ProductEntry != null && s.ProductEntry.Product != null ? s.ProductEntry.Product.Code : null,
                    ProductName = s.ProductEntry != null && s.ProductEntry.Product != null ? s.ProductEntry.Product.Name : null
                })
                .OrderByDescending(x => x.CreateDate)
                .ToListAsync();

            var saleIds = sales.Select(s => s.Id).ToList();

            var paidBySale = await _db.CashTransactions
                .AsNoTracking()
                .Where(t => t.ProductSalesId != null && saleIds.Contains(t.ProductSalesId.Value))
                .Where(t => t.CashTransactionType != null && t.CashTransactionType.TransactionSign == 1)
                .GroupBy(t => t.ProductSalesId!.Value)
                .Select(g => new { SaleId = g.Key, Paid = g.Sum(x => (decimal)(x.Amount ?? 0m)) })
                .ToListAsync();

            var paidLookup = paidBySale.ToDictionary(x => x.SaleId, x => x.Paid);

            var saleRows = sales.Select(s =>
            {
                var total = (decimal)(s.TotalPrice ?? 0m);
                var paid = paidLookup.TryGetValue(s.Id, out var pv) ? pv : 0m;
                var remain = total - paid; if (remain < 0) remain = 0;
                return new
                {
                    id = s.Id,
                    date = s.CreateDate,
                    productCode = s.ProductCode,
                    productName = s.ProductName,
                    amount = s.Amount ?? 0,
                    unitPrice = s.SalesPrice ?? 0,
                    netPrice = s.NetPrice ?? 0,
                    totalPrice = total,
                    paid,
                    remaining = remain
                };
            }).OrderByDescending(x => x.date).ToList();

            var totalSales = saleRows.Sum(x => x.totalPrice);
            var totalPaid = saleRows.Sum(x => x.paid);
            var remaining = totalSales - totalPaid; if (remaining < 0) remaining = 0;

            var payments = await _db.CashTransactions
                .AsNoTracking()
                .Where(t => t.ProductSalesId != null && saleIds.Contains(t.ProductSalesId.Value))
                .Where(t => t.CashTransactionType != null && t.CashTransactionType.TransactionSign == 1)
                .Select(t => new
                {
                    id = t.Id,
                    createDate = t.CreateDate,
                    amount = t.Amount,
                    description = t.Description,
                    saleId = t.ProductSalesId!.Value,
                    cashType = t.CashTransactionType != null ? t.CashTransactionType.Name : null
                })
                .OrderByDescending(x => x.createDate)
                .ToListAsync();

            return Json(new
            {
                header = new
                {
                    id = cust.Id,
                    name = ($"{cust.FirstName ?? ""} {cust.LastName ?? ""}").Trim(),
                    phone = cust.PhoneNumber,
                    email = cust.EmailAdress
                },
                summary = new
                {
                    totalSales,
                    totalPaid,
                    remaining
                },
                sales = saleRows,
                payments
            });
        }
    }
}
