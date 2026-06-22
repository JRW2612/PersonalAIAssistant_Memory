using PersonalAIAssistant.Memory.Core.Exceptions;

namespace PersonalAIAssistant.Memory.Core.Domains.ValueObjects
{
    public readonly struct MemoryId
    {
        public Guid Value { get; }
        public MemoryId(Guid value)
        {
            if (value == Guid.Empty) throw new DomainException("MemoryId cannot be empty.");
            Value = value;
        }
        public static MemoryId New() => new MemoryId(Guid.NewGuid());
        public override string ToString() => Value.ToString();
        public static implicit operator Guid(MemoryId id) => id.Value;
        public static implicit operator MemoryId(Guid g) => new MemoryId(g);
    }
}
