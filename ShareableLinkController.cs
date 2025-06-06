using BOBDrive.Models;
using BOBDrive.ViewModels;
using BOBDrive.Controllers;
using System;
using System.Linq;
using System.Web.Mvc;
using System.Web.Security;

namespace BOBDrive.Controllers
{
    public class ShareableLinkController : BaseController
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(ShareLinkCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // This case should ideally be handled by client-side validation or return a proper error JSON
                return Json(new { success = false, message = "Invalid data." });
            }

            var file = db.Files.Find(model.FileId);
            if (file == null || file.IsProcessing)
            {
                return Json(new { success = false, message = "File not found or is processing." });
            }

            var token = Guid.NewGuid().ToString("N"); // N format = 32 digits without hyphens
            var link = new ShareableLink
            {
                FileId = model.FileId,
                Token = token,
                PasswordHash = !string.IsNullOrWhiteSpace(model.Password) ?
                               FormsAuthentication.HashPasswordForStoringInConfigFile(model.Password.Trim(), "SHA1") : null,
                CreatedAt = DateTime.Now,
                // ExpiresAt = DateTime.Now.AddDays(7) // Example: Set an expiration date
            };

            db.ShareableLinks.Add(link);
            db.SaveChanges();

            var generatedLink = Url.Action("Access", "ShareableLink", new { token = token }, Request.Url.Scheme);

            return Json(new { success = true, link = generatedLink });
        }

        [HttpGet]
        public ActionResult Access(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return HttpNotFound("Link token is missing.");
            }

            var link = db.ShareableLinks.Include("File") // Eager load the File entity
                           .FirstOrDefault(l => l.Token == token);

            if (link == null)
            {
                return HttpNotFound("Invalid or expired link.");
            }

            if (link.ExpiresAt.HasValue && link.ExpiresAt < DateTime.Now)
            {
                // Optionally remove the expired link from DB
                // db.ShareableLinks.Remove(link);
                // db.SaveChanges();
                return HttpNotFound("This link has expired.");
            }

            if (link.File == null || link.File.IsProcessing)
            {
                return HttpNotFound("The associated file is not available or is being processed.");
            }


            if (!string.IsNullOrEmpty(link.PasswordHash))
            {
                // If password protected, show password entry view
                var viewModel = new ShareLinkAccessViewModel { Link = new ShareableLink { Token = link.Token } }; // Only pass necessary info
                return View("PasswordProtect", viewModel);
            }

            // No password, proceed to download
            return RedirectToAction("Download", "File", new { id = link.FileId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult PasswordProtect(string token, string password)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(password))
            {
                TempData["ErrorMessage"] = "Token or password cannot be empty.";
                // Re-render the view with an error
                var errorViewModel = new ShareLinkAccessViewModel { Link = new ShareableLink { Token = token }, ErrorMessage = "Token or password cannot be empty." };
                return View(errorViewModel);
            }

            var link = db.ShareableLinks.FirstOrDefault(l => l.Token == token);
            if (link == null || string.IsNullOrEmpty(link.PasswordHash)) // Also check if it's supposed to have a password
            {
                return HttpNotFound("Invalid link or no password required.");
            }

            if (link.ExpiresAt.HasValue && link.ExpiresAt < DateTime.Now)
            {
                return HttpNotFound("This link has expired.");
            }

            var hashedPasswordAttempt = FormsAuthentication.HashPasswordForStoringInConfigFile(password.Trim(), "SHA1");

            if (link.PasswordHash == hashedPasswordAttempt)
            {
                return RedirectToAction("Download", "File", new { id = link.FileId });
            }
            else
            {
                var viewModel = new ShareLinkAccessViewModel { Link = new ShareableLink { Token = token }, ErrorMessage = "Invalid password." };
                return View(viewModel);
            }
        }
    }
}