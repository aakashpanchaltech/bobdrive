using System.Web;
using System.Web.Mvc;

namespace BOBDrive
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());

            // This makes all actions require authorization by default
            filters.Add(new AuthorizeAttribute()); 
            
            // You would then use [AllowAnonymous] on public actions like Login/Register
        }
    }
}
