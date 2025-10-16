using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorkbookManagement.Models;

namespace WorkbookManagement.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Company> Companies => Set<Company>();
        public DbSet<WorkbookSubmission> WorkbookSubmissions => Set<WorkbookSubmission>();
        public DbSet<Submission> Submissions => Set<Submission>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Store enums as strings where desired (readable schema)
            var workbookTypeConv = new EnumToStringConverter<WorkbookType>();
            var submissionBundleStatusConv = new EnumToStringConverter<SubmissionBundleStatus>();

            // --- ApplicationUser → Company (tenant link) ---
            builder.Entity<ApplicationUser>(u =>
            {
                u.HasOne(x => x.Company)
                 .WithMany(c => c.Users)
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // --- Submission (bundle) ---
            builder.Entity<Submission>(s =>
            {
                // Parent bundle status as string
                s.Property(x => x.Status).HasConversion(submissionBundleStatusConv);

                s.HasOne(x => x.OwnerUser)
                 .WithMany()
                 .HasForeignKey(x => x.OwnerUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                s.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);

                // NEW: optional link to the admin who decided (approve/reject)
                s.HasOne(x => x.DecidedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.DecidedByUserId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // --- WorkbookSubmission (child workbooks) ---
            builder.Entity<WorkbookSubmission>(e =>
            {
                // Workbook type as string
                e.Property(x => x.WorkbookType).HasConversion(workbookTypeConv);

                // NOTE: Do not convert e.Status here (we keep existing numeric values)
                e.HasOne(x => x.User)
                 .WithMany(u => u.WorkbookSubmissions)
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);

                // Link to parent submission (bundle)
                e.HasOne(x => x.Submission)
                 .WithMany(s => s.Workbooks)
                 .HasForeignKey(x => x.SubmissionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // Company unique name
            builder.Entity<Company>()
                   .HasIndex(c => c.Name)
                   .IsUnique();
        }
    }
}
