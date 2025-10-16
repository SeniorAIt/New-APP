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

        // company file uploads
        public DbSet<CompanyDocument> CompanyDocuments => Set<CompanyDocument>();

        // dashboard features
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();

        // multi-company targets for announcements
        public DbSet<AnnouncementCompany> AnnouncementCompanies => Set<AnnouncementCompany>();

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
                s.Property(x => x.Status).HasConversion(submissionBundleStatusConv);
                s.Property(x => x.LastDecisionStatus).HasConversion(submissionBundleStatusConv);

                s.HasOne(x => x.OwnerUser)
                 .WithMany()
                 .HasForeignKey(x => x.OwnerUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                s.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);

                s.HasOne(x => x.DecidedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.DecidedByUserId)
                 .OnDelete(DeleteBehavior.SetNull);

                s.HasOne(x => x.LastDecidedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.LastDecidedByUserId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // --- WorkbookSubmission (child workbooks) ---
            builder.Entity<WorkbookSubmission>(e =>
            {
                e.Property(x => x.WorkbookType).HasConversion(workbookTypeConv);

                e.HasOne(x => x.User)
                 .WithMany(u => u.WorkbookSubmissions)
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Submission)
                 .WithMany(s => s.Workbooks)
                 .HasForeignKey(x => x.SubmissionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // --- CompanyDocument (uploads) ---
            builder.Entity<CompanyDocument>(d =>
            {
                d.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Cascade);

                d.HasOne(x => x.UploadedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.UploadedByUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                // NEW: persist DocumentType (enum) as string (readable)
                d.Property(x => x.DocumentType).HasConversion<string>();

                // Existing index + NEW filter-friendly index with type
                d.HasIndex(x => new { x.CompanyId, x.UploadedAtUtc });
                d.HasIndex(x => new { x.CompanyId, x.DocumentType, x.UploadedAtUtc });
            });

            // --- Announcements (dashboard) ---
            builder.Entity<Announcement>(a =>
            {
                a.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);

                a.HasOne(x => x.AuthorUser)
                 .WithMany()
                 .HasForeignKey(x => x.AuthorUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                a.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc });
            });

            // Announcement ⇄ Company (targets)
            builder.Entity<AnnouncementCompany>(b =>
            {
                b.HasKey(x => new { x.AnnouncementId, x.CompanyId });

                b.HasOne(x => x.Announcement)
                 .WithMany(a => a.Targets)
                 .HasForeignKey(x => x.AnnouncementId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // --- CalendarEvents (dashboard) ---
            builder.Entity<CalendarEvent>(e =>
            {
                e.HasOne(x => x.Company)
                 .WithMany()
                 .HasForeignKey(x => x.CompanyId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.CreatedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.CreatedByUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.CompanyId, x.StartUtc });
            });

            builder.Entity<Company>()
                   .HasIndex(c => c.Name)
                   .IsUnique();
        }
    }
}
