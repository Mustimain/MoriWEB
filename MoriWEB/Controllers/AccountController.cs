using Microsoft.AspNetCore.Mvc;
using MoriWEB.DatabaseContext;

namespace MoriWEB.Controllers
{
    public class AccountController : Controller
    {
        private readonly MoriDbContext _db;

        public AccountController(MoriDbContext db)
        {
            _db = db;
        }
        public IActionResult Index()
        {
            return View();
        } 
        public IActionResult MyAccount()
        {
            return View();
        }
    }
}
