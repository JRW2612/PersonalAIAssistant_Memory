// PersonalAIAssistant.Memory.Infrastructure.EF/ReadModelDbContext.cs
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;

namespace PersonalAIAssistant.Memory.Infrastructure.Context
{
    public class ReadModelDbContext : DbContext
    {
        public ReadModelDbContext(DbContextOptions<ReadModelDbContext> options) : base(options) { }

        public DbSet<MemoryReadModelEntity> MemoryReadModels { get; set; } = null!;
        public DbSet<ProcessedEventEntity> ProcessedEvents { get; set; } = null!;
        public DbSet<ProcessingLockEntity> ProcessingLocks { get; set; } = null!;
        public DbSet<ProviderConnectionEntity> ProviderConnections { get; set; } = null!;
        public DbSet<ProviderAuditLogEntity> ProviderAuditLogs { get; set; } = null!;
        public DbSet<WorkloadIdentityEntity> WorkloadIdentities { get; set; } = null!;
        public DbSet<TemporalFactEntity> TemporalFacts { get; set; } = null!;
        public DbSet<PersonalAIAssistant.Memory.Core.Models.EmployeeRecord> EmployeeRecords { get; set; } = null!;
        public DbSet<AccessAssignmentEntity> AccessAssignments { get; set; } = null!;
        public DbSet<PersonalAIAssistant.Memory.Infrastructure.EF.Entities.EfOutboxMessage> OutboxMessages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MemoryReadModelEntity>(b =>
            {
                b.HasKey(x => x.MemoryId);
                b.Property(x => x.StreamId).IsRequired().HasMaxLength(200);
                b.Property(x => x.Summary).IsRequired();
                b.Property(x => x.TokenCount).IsRequired();
                b.Property(x => x.Importance).IsRequired().HasConversion<int>();
                b.Property(x => x.CreatedAt).IsRequired();
                b.Property(x => x.Archived).IsRequired();
            });

            modelBuilder.Entity<ProcessedEventEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.AggregateId, x.Version }).IsUnique();
                b.Property(x => x.ProcessedAt).IsRequired();
            });

            modelBuilder.Entity<ProcessingLockEntity>(b =>
            {
                b.HasKey(x => x.MemoryId);
                b.Property(x => x.LockedAt).IsRequired();
            });

            modelBuilder.Entity<ProviderConnectionEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
                b.Property(x => x.UserId).IsRequired().HasMaxLength(128);
                b.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
                b.Property(x => x.Provider).IsRequired().HasMaxLength(64);
                b.Property(x => x.EncryptedAccessToken).IsRequired();
                b.Property(x => x.Status).IsRequired().HasConversion<int>();
                b.Property(x => x.CreatedAtUtc).IsRequired();
                b.Property(x => x.UpdatedAtUtc).IsRequired();
            });

            modelBuilder.Entity<ProviderAuditLogEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.UserId, x.TimestampUtc });
                b.Property(x => x.UserId).IsRequired().HasMaxLength(128);
                b.Property(x => x.Provider).IsRequired().HasMaxLength(64);
                b.Property(x => x.EventType).IsRequired().HasMaxLength(64);
                b.Property(x => x.TimestampUtc).IsRequired();
            });

            modelBuilder.Entity<WorkloadIdentityEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.HasIndex(x => x.ClientId).IsUnique();
                b.HasIndex(x => new { x.TenantId, x.CloudPlatform });
                b.Property(x => x.ClientId).IsRequired().HasMaxLength(200);
                b.Property(x => x.Name).IsRequired().HasMaxLength(200);
                b.Property(x => x.CloudPlatform).IsRequired().HasMaxLength(50);
                b.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
                b.Property(x => x.AllowedScopes).IsRequired().HasMaxLength(500);
                b.Property(x => x.AuthType).HasConversion<int>();
                b.Property(x => x.Status).HasConversion<int>();
                b.Property(x => x.CreatedAtUtc).IsRequired();
                b.Property(x => x.UpdatedAtUtc).IsRequired();
            });

            modelBuilder.Entity<TemporalFactEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.UserId, x.Subject, x.Predicate, x.IsCurrent });
                b.HasIndex(x => new { x.UserId, x.IsCurrent });
                b.Property(x => x.UserId).IsRequired().HasMaxLength(128);
                b.Property(x => x.Subject).IsRequired().HasMaxLength(256);
                b.Property(x => x.Predicate).IsRequired().HasMaxLength(128);
                b.Property(x => x.Object).IsRequired().HasMaxLength(1000);
                b.Property(x => x.ValidFromUtc).IsRequired();
                b.Property(x => x.IsCurrent).IsRequired();
            });

            modelBuilder.Entity<PersonalAIAssistant.Memory.Core.Models.EmployeeRecord>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.UserId).IsRequired().HasMaxLength(128);
                b.Property(x => x.FullName).IsRequired().HasMaxLength(256);
                b.Property(x => x.Department).IsRequired().HasMaxLength(128);
                b.Property(x => x.Salary).HasPrecision(18, 2);
                b.Property(x => x.SocialSecurityNumber).HasMaxLength(64);
                b.HasIndex(x => x.UserId);
            });

            modelBuilder.Entity<AccessAssignmentEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.HasIndex(x => x.IdempotencyKey).IsUnique();
                b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
                b.Property(x => x.UserId).IsRequired().HasMaxLength(128);
                b.Property(x => x.TargetResourceId).IsRequired().HasMaxLength(256);
                b.Property(x => x.PermissionLevel).IsRequired().HasMaxLength(64);
                b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(256);
                b.Property(x => x.Status).HasConversion<int>();
            });

            modelBuilder.Entity<PersonalAIAssistant.Memory.Infrastructure.EF.Entities.EfOutboxMessage>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.MessageType).IsRequired().HasMaxLength(200);
                b.Property(o => o.Payload).IsRequired();
                b.Property(o => o.OccurredAt).IsRequired();
                b.HasIndex(o => o.DispatchedAt);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
