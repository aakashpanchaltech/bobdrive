using BOBDrive.Models;
using BOBDrive.ViewModels;
using System.Linq;
using System.Web.Mvc;
using System;
using BOBDrive.Controllers;


namespace BOBDrive.Controllers
{
    public class HomeController : BaseController // Inherit from BaseController
    {
        public ActionResult Index(int? folderId)
        {
            Folder currentFolder;
            int? parentOfCurrentFolderId = null;

            if (folderId.HasValue)
            {
                currentFolder = db.Folders.Find(folderId.Value);
                if (currentFolder == null)
                {
                    // Handle case where folderId is invalid, redirect to root
                    return RedirectToAction("Index", new { folderId = (int?)null });
                }
                parentOfCurrentFolderId = currentFolder.ParentFolderId;
            }
            else
            {
                // Root folder logic
                currentFolder = db.Folders.FirstOrDefault(f => f.ParentFolderId == null && f.Name == "Root");
                if (currentFolder == null)
                {
                    currentFolder = new Folder { Name = "Root", CreatedAt = DateTime.Now, ParentFolderId = null };
                    db.Folders.Add(currentFolder);
                    db.SaveChanges();
                }
            }

            var viewModel = new FolderViewModel
            {
                CurrentFolder = currentFolder,
                SubFolders = db.Folders.Where(f => f.ParentFolderId == currentFolder.Id).OrderBy(f => f.Name).ToList(),
                Files = db.Files.Where(f => f.FolderId == currentFolder.Id && !f.IsProcessing).OrderBy(f => f.Name).ToList(),
                ParentOfCurrentFolderId = parentOfCurrentFolderId
            };

            return View(viewModel);
        }
    }
}