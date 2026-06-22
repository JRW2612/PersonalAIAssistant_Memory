// PersonalAIAssistant.Memory.Infrastructure.EF/ReadModelDbContext.cs
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;

namespace PersonalAIAssistant.Memory.Infrastructure.EF
{
    public class ReadModelDbContext : DbContext
    {
        public ReadModelDbContext(DbContextOptions<ReadModelDbContext> options) : base(options) { }

        public DbSet<MemoryReadModelEntity> MemoryReadModels { get; set; } = null!;
        public DbSet<ProcessedEventEntity> ProcessedEvents { get; set; } = null!;
        public DbSet<ProcessingLockEntity> ProcessingLocks { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MemoryReadModelEntity>(b =>
            {
                b.HasKey(x => x.MemoryId);
                b.Property(x => x.StreamId).IsRequired().HasMaxLength(200);
                b.Property(x => x.Summary).IsRequired();
                b.Property(x => x.TokenCount).IsRequired();
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

            base.OnModelCreating(modelBuilder);
        }
    }
}
