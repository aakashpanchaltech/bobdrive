using BOBDrive.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BOBDrive.ViewModels
{
    public class FolderViewModel
    {
        public Folder CurrentFolder { get; set; }
        public List<Folder> SubFolders { get; set; }
        public List<File> Files { get; set; }
        public int? ParentOfCurrentFolderId { get; set; } // For ".." navigation
    }
}