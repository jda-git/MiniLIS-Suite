using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Common;
using MiniLIS.Domain.Identity;
using MiniLIS.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MiniLIS.Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
    {
        private readonly ICurrentUserService? _currentUserService;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentUserService? currentUserService = null) : base(options)
        {
            _currentUserService = currentUserService;
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
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

            modelBuilder.Entity<SamplePanel>()
                .HasOne(sp => sp.Sample)
                .WithMany(s => s.Panels)
                .HasForeignKey(sp => sp.SampleId);

            modelBuilder.Entity<SamplePanel>()
                .HasOne(sp => sp.Panel)
                .WithMany()
                .HasForeignKey(sp => sp.PanelId);

            modelBuilder.Entity<SampleReport>()
                .HasOne(r => r.Sample)
                .WithOne(s => s.Report)
                .HasForeignKey<SampleReport>(r => r.SampleId);
        }

        public override int SaveChanges()
        {
            int? currentUserId = null;
            string? currentUsername = null;
            string? actionContext = null;

            if (_currentUserService != null)
            {
                try
                {
                    currentUserId = Task.Run(() => _currentUserService.GetUserIdAsync()).GetAwaiter().GetResult();
                    currentUsername = Task.Run(() => _currentUserService.GetUsernameAsync()).GetAwaiter().GetResult();
                    actionContext = _currentUserService.ActionContext;
                }
                catch
                {
                    // Fallback
                }
            }

            ApplyAuditing(currentUserId);
            var auditEntries = BeforeSaveChanges(currentUserId, currentUsername, actionContext);
            
            var result = base.SaveChanges();
            
            AfterSaveChanges(auditEntries).GetAwaiter().GetResult();
            return result;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            int? currentUserId = null;
            string? currentUsername = null;
            string? actionContext = null;

            if (_currentUserService != null)
            {
                currentUserId = await _currentUserService.GetUserIdAsync();
                currentUsername = await _currentUserService.GetUsernameAsync();
                actionContext = _currentUserService.ActionContext;
            }

            ApplyAuditing(currentUserId);
            var auditEntries = BeforeSaveChanges(currentUserId, currentUsername, actionContext);

            var result = await base.SaveChangesAsync(cancellationToken);

            await AfterSaveChanges(auditEntries);
            return result;
        }

        private void ApplyAuditing(int? currentUserId)
        {
            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAtUtc = DateTime.UtcNow;
                        entry.Entity.CreatedBy = entry.Entity.CreatedBy == 0 ? (currentUserId ?? 1) : entry.Entity.CreatedBy;
                        if (entry.Entity is Sample addedSample)
                        {
                            addedSample.RowVersion = Guid.NewGuid().ToByteArray();
                        }
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
                        entry.Entity.UpdatedBy = entry.Entity.UpdatedBy ?? currentUserId ?? 1;
                        if (entry.Entity is Sample modifiedSample)
                        {
                            modifiedSample.RowVersion = Guid.NewGuid().ToByteArray();
                        }
                        break;
                }
            }
        }

        private List<AuditLogTemp> BeforeSaveChanges(int? currentUserId, string? currentUsername, string? actionContext)
        {
            ChangeTracker.DetectChanges();
            var auditEntries = new List<AuditLogTemp>();

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                if (entry.Entity is not AuditableEntity)
                    continue;

                var auditEntry = new AuditLogTemp(entry)
                {
                    EntityName = entry.Entity.GetType().Name,
                    UserId = currentUserId,
                    Username = currentUsername,
                    ActionContext = actionContext,
                    Action = entry.State switch
                    {
                        EntityState.Added => "Create",
                        EntityState.Modified => "Update",
                        EntityState.Deleted => "Delete",
                        _ => "Unknown"
                    }
                };

                auditEntries.Add(auditEntry);

                foreach (var property in entry.Properties)
                {
                    string propertyName = property.Metadata.Name;
                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.KeyValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                            break;

                        case EntityState.Deleted:
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            break;

                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                if (propertyName == "RowVersion" || propertyName == "UpdatedAtUtc" || propertyName == "UpdatedBy")
                                    continue;

                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }
            }

            return auditEntries;
        }

        private async Task AfterSaveChanges(List<AuditLogTemp> auditEntries)
        {
            if (auditEntries == null || auditEntries.Count == 0)
                return;

            foreach (var auditEntry in auditEntries)
            {
                if (auditEntry.Action == "Create")
                {
                    foreach (var prop in auditEntry.Entry.Properties)
                    {
                        if (prop.Metadata.IsPrimaryKey())
                        {
                            auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                        }
                    }
                }
                
                AuditLogs.Add(auditEntry.ToAuditLog());
            }

            await base.SaveChangesAsync();
        }
    }

    public class AuditLogTemp
    {
        public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry { get; }
        public string EntityName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public string? Username { get; set; }
        public string? ActionContext { get; set; }
        public Dictionary<string, object?> KeyValues { get; } = new();
        public Dictionary<string, object?> OldValues { get; } = new();
        public Dictionary<string, object?> NewValues { get; } = new();

        public AuditLogTemp(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            Entry = entry;
        }

        public AuditLog ToAuditLog()
        {
            var log = new AuditLog
            {
                EntityName = EntityName,
                EntityId = JsonSerializer.Serialize(KeyValues),
                Action = Action,
                UserId = UserId,
                Username = Username,
                TimestampUtc = DateTime.UtcNow,
                ActionContext = ActionContext
            };

            var changesList = new List<string>();
            foreach (var prop in NewValues.Keys)
            {
                var oldVal = OldValues.ContainsKey(prop) ? OldValues[prop] : null;
                var newVal = NewValues[prop];
                changesList.Add($"{prop}: {oldVal ?? "null"} -> {newVal ?? "null"}");
            }

            if (Action == "Create")
            {
                log.Changes = "Creado con valores: " + JsonSerializer.Serialize(NewValues);
            }
            else if (Action == "Delete")
            {
                log.Changes = "Eliminado con valores: " + JsonSerializer.Serialize(OldValues);
            }
            else
            {
                log.Changes = string.Join(", ", changesList);
            }

            return log;
        }
    }
}
