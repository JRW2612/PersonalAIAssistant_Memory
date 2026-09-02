using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Infrastructure.EF.Entities;

namespace PersonalAIAssistant.Memory.Infrastructure.EF
{
    public class EventStoreDbContext : DbContext
    {
        public EventStoreDbContext(DbContextOptions<EventStoreDbContext> options) : base(options) { }

        public DbSet<EventEntity> Events { get; set; } = null!;
        public DbSet<EfOutboxMessage> OutboxMessages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EventEntity>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.StreamId).IsRequired().HasMaxLength(200);
                b.Property(e => e.EventType).IsRequired().HasMaxLength(200);
                b.Property(e => e.Payload).IsRequired();
                b.HasIndex(e => new { e.StreamId, e.Version }).IsUnique();
            });

            modelBuilder.Entity<EfOutboxMessage>(b =>
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
