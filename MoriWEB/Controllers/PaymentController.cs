using Microsoft.AspNetCore.Mvc;
using MoriWEB.DatabaseContext;

namespace MoriWEB.Controllers
{
    public class PaymentController : Controller
    {
        private readonly MoriDbContext _db;

        public PaymentController(MoriDbContext db)
        {
            _db = db;
        }
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult Payments()
        {
            return View();
        }
    }
}
