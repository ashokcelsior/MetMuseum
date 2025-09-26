using Metmuseum.Models;
using Microsoft.EntityFrameworkCore;

namespace Metmuseum.Data
{
    public class MetMuseumContext: DbContext
    {
        public MetMuseumContext()
        {
        }

        public MetMuseumContext(DbContextOptions<MetMuseumContext>? options = null)
             : base(options ?? new DbContextOptions<MetMuseumContext>())
        {
        }
        public DbSet<MetMuseumObject> MetObjects { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseSqlite("Data Source=metmuseum.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MetMuseumObject>()
                .HasKey(o => o.ObjectID);
            modelBuilder.Entity<MetMuseumObject>()
                .HasIndex(o => o.Title);
        }
    }
}
