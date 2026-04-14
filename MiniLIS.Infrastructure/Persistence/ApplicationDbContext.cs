using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Common;
using MiniLIS.Domain.Identity;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace MiniLIS.Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<ClinicalRequest> ClinicalRequests => Set<ClinicalRequest>();
        public DbSet<Sample> Samples => Set<Sample>();
        public DbSet<SamplePanel> SamplePanels => Set<SamplePanel>();
        public DbSet<Panel> Panels => Set<Panel>();
        public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
        public DbSet<Marker> Markers => Set<Marker>();
        public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();
        public DbSet<TemplateMarker> TemplateMarkers => Set<TemplateMarker>();
        public DbSet<SampleReport> SampleReports => Set<SampleReport>();
        public DbSet<ReportMarkerValue> ReportMarkerValues => Set<ReportMarkerValue>();
        public DbSet<ReportSignatory> ReportSignatories => Set<ReportSignatory>();
        public DbSet<TemplateConclusion> TemplateConclusions => Set<TemplateConclusion>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Relationships
            modelBuilder.Entity<Sample>()
                .HasOne(s => s.ClinicalRequest)
                .WithMany(r => r.Samples)
                .HasForeignKey(s => s.ClinicalRequestId);

            modelBuilder.Entity<ClinicalRequest>()
                .HasOne(r => r.Patient)
                .WithMany(p => p.Requests)
                .HasForeignKey(r => r.PatientId);
        }

        public override int SaveChanges()
        {
            ApplyAuditing();
            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditing();
            return await base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyAuditing()
        {
            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAtUtc = DateTime.UtcNow;
                        // For now we use 1 as system user, in Phase 12 we will inject the user id.
                        entry.Entity.CreatedBy = entry.Entity.CreatedBy == 0 ? 1 : entry.Entity.CreatedBy;
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
                        entry.Entity.UpdatedBy = entry.Entity.UpdatedBy ?? 1;
                        break;
                }
            }
        }
    }
}
