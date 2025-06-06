using BOBDrive.Models; 
using BOBDrive.ViewModels; 
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Security;
using System.Web.Helpers;
using System.Data.Entity;

namespace BOBDrive.Controllers 
{
    public class AccountController : BaseController // Assumes BaseController is in BOBDrive.Controllers
    {
        private readonly ExternalDbContext _externalDb = new ExternalDbContext();

        [AllowAnonymous]
        public ActionResult Register()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            if (ModelState.IsValid)
            {
                var externalUser = await _externalDb.ExternalUsers.FindAsync(model.UserId);
                if (externalUser == null)
                {
                    ModelState.AddModelError("UserId", "This User ID is not found in our records. Please ensure you are using your correct Employee ID.");
                    return View(model);
                }

                var existingAppUser = await db.Users.FirstOrDefaultAsync(u => u.ExternalUserId == model.UserId);
                if (existingAppUser != null)
                {
                    ModelState.AddModelError("UserId", "This User ID is already registered in this application. Please try logging in.");
                    return View(model);
                }

                var user = new User
                {
                    ExternalUserId = externalUser.UserId,
                    Username = externalUser.UserId,
                    PasswordHash = Crypto.HashPassword(model.Password),
                    FullName = externalUser.EmployeeName,
                    ExternalRoleId = externalUser.RoleId,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(user);
                await db.SaveChangesAsync();

                FormsAuthentication.SetAuthCookie(user.ExternalUserId, createPersistentCookie: false);
                Session["UserFullName"] = user.FullName;

                TempData["SuccessMessage"] = "Registration successful! You are now logged in to BOBDrive.";
                return RedirectToAction("Index", "Home");
            }
            return View(model);
        }

        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            if (ModelState.IsValid)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalUserId == model.UserId);
                if (user != null && Crypto.VerifyHashedPassword(user.PasswordHash, model.Password))
                {
                    FormsAuthentication.SetAuthCookie(user.ExternalUserId, model.RememberMe);
                    user.LastLoginAt = DateTime.UtcNow;
                    db.Entry(user).State = EntityState.Modified;
                    await db.SaveChangesAsync();

                    Session["UserFullName"] = user.FullName;

                    if (Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }
                    TempData["SuccessMessage"] = "Login successful to BOBDrive!";
                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError("", "Invalid User ID or password.");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();
            Response.Cookies.Add(new System.Web.HttpCookie("ASP.NET_SessionId", ""));

            TempData["SuccessMessage"] = "You have been successfully logged out from BOBDrive.";
            return RedirectToAction("Login", "Account");
        }

        [Authorize]
        public ActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var currentExternalUserId = User.Identity.Name;
                var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalUserId == currentExternalUserId);

                if (user == null)
                {
                    FormsAuthentication.SignOut();
                    return RedirectToAction("Login", "Account");
                }

                if (Crypto.VerifyHashedPassword(user.PasswordHash, model.OldPassword))
                {
                    user.PasswordHash = Crypto.HashPassword(model.NewPassword);
                    db.Entry(user).State = EntityState.Modified;
                    await db.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Your password has been changed successfully for BOBDrive.";
                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError("OldPassword", "Incorrect current password.");
            }
            return View(model);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _externalDb.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}