using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RSSAPI.Models;

namespace RSSAPI.Data
{
    public class ScreenDbContext(DbContextOptions<ScreenDbContext> options, IConfiguration configuration) : DbContext(options)
    {
        private readonly IConfiguration configuration = configuration;

        public DbSet<Screen> TblScreens { get; set; } = null!;
        public DbSet<Logs> TblLogs { get; set; } = null!;
        public DbSet<User> TblUsers { get; set; } = null!;

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (optionsBuilder.IsConfigured)
				return;

			string? connectionString = configuration.GetConnectionString("ScreenDB");

			if (string.IsNullOrEmpty(connectionString))
				throw new InvalidOperationException("Database connection string is missing or empty.");

			optionsBuilder.UseSqlServer(connectionString);
		}


		protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Logs>().ToTable("tblLogs", t => t.ExcludeFromMigrations());

            modelBuilder.Entity<Screen>(entity =>
            {
                entity.ToTable("tblScreens");
                entity.HasKey(s => s.MacAddress);

                entity.Property(s => s.Address)
                      .HasMaxLength(255)
                      .IsRequired(false);

                entity.Property(s => s.OperatingSystem) 
                      .HasMaxLength(100)
                      .IsRequired(false);
                entity.Property(s => s.StartupEnabled)
                      .HasColumnName("StartupEnabled")
                      .IsRequired(false);

            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("tblUsers");
                entity.HasKey(e => e.UserID);
                entity.HasIndex(e => e.Username).IsUnique();
            });
        }
    }
}
