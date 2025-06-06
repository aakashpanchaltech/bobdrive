using System.Data.Entity;
using BOBDrive.Models.External; // Changed namespace

namespace BOBDrive.Models // Changed namespace
{
    public class ExternalDbContext : DbContext
    {
        // Connection string name "ExternalDbContext" is used from web.config
        public ExternalDbContext()
            : base("name=ExternalDbContext")
        {
        }

        public virtual DbSet<ExternalUser> ExternalUsers { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}