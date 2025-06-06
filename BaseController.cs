using BOBDrive.Models;
using System.Web.Mvc;

namespace BOBDrive.Controllers
{
    public class BaseController : Controller
    {
        protected CloudStorageDbContext db = new CloudStorageDbContext();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}