using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoriWEB.DatabaseContext;
using MoriWEB.Models;

namespace MoriWEB.Controllers
{
    public class CustomerController : Controller
    {
        private readonly MoriDbContext _db;

        public CustomerController(MoriDbContext db)
        {
            _db = db;
        }
        public IActionResult Index()
        {
            return View();
        } 
        
        public IActionResult Customers()
        {
            return View();
        }


        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var list = await _db.Customers.AsNoTracking()
                .Where(c => q == "" ||
                            (c.FirstName + " " + c.LastName).ToLower().Contains(q) ||
                            c.PhoneNumber.ToLower().Contains(q))
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
                .Select(c => new {
                    c.Id,
                    c.TcNo,
                    c.FirstName,
                    c.LastName,
                    c.PhoneNumber,
                    c.EmailAdress,
                    c.Adress,
                    c.CargoAdress,
                    c.TaxCompanyName,
                    c.TaxOffice,
                    c.TaxNo,
                    c.TaxAdress
                })
                .ToListAsync();

            return Json(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Customer dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Telefon tekil
            var exists = await _db.Customers.AnyAsync(x => x.PhoneNumber == dto.PhoneNumber);
            if (exists) return Conflict(new { field = "PhoneNumber", message = "Bu telefonla kayıt zaten var." });

            _db.Customers.Add(dto);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true, id = dto.Id });
        }
 
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] Customer dto)
        {
            if (dto.Id <= 0) return BadRequest("Geçersiz Id");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var current = await _db.Customers.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (current == null) return NotFound();

            // Telefon tekil (kendisi hariç)
            var phoneExists = await _db.Customers
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber && x.Id != dto.Id);
            if (phoneExists) return Conflict(new { field = "PhoneNumber", message = "Bu telefonla başka bir kayıt var." });

            current.TcNo = dto.TcNo;
            current.FirstName = dto.FirstName;
            current.LastName = dto.LastName;
            current.PhoneNumber = dto.PhoneNumber;
            current.EmailAdress = dto.EmailAdress;
            current.Adress = dto.Adress;
            current.CargoAdress = dto.CargoAdress;
            current.TaxCompanyName = dto.TaxCompanyName;
            current.TaxOffice = dto.TaxOffice;
            current.TaxNo = dto.TaxNo;
            current.TaxAdress = dto.TaxAdress;

            await _db.SaveChangesAsync();
            return Ok(new { ok = true, id = current.Id });
        }

        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] Customer dto)
        {
            if (dto == null || dto.Id <= 0)
                return BadRequest("Geçersiz id");

            var entity = await _db.Customers.FirstOrDefaultAsync(x => x.Id == dto.Id);
            if (entity == null)
                return NotFound();

            try
            {
                _db.Customers.Remove(entity);
                await _db.SaveChangesAsync();
                return Ok(new { ok = true });
            }
            catch (DbUpdateException ex)
            {
                // Örn. FK kısıtı nedeniyle silinemiyorsa
                return Conflict(new { ok = false, message = "Kayıt silinemedi. İlişkili kayıtlar olabilir.", detail = ex.Message });
            }
        }
    }
}
