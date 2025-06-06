using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace BOBDrive.ViewModels
{
    public class ShareLinkCreateViewModel
    {
        [Required]
        public int FileId { get; set; }
        public string FileName { get; set; } // To display in modal
        public string Password { get; set; }
    }
}