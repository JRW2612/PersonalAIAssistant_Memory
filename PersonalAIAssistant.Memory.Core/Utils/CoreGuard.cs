using PersonalAIAssistant.Memory.Core.Exceptions;

namespace PersonalAIAssistant.Memory.Core.Utils
{
    public static class CoreGuard
    {
        public static void NotNullOrWhiteSpace(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException($"{name} must not be empty.");
        }

        public static void NotNull<T>(T value, string name) where T : class
        {
            if (value is null) throw new PersonalAIAssistant.Memory.Core.Exceptions.DomainException($"{name} must not be null.");
        }

        // New: for value types (enums, structs)
        public static void NotDefault<T>(T value, string name) where T : struct
        {
            if (EqualityComparer<T>.Default.Equals(value, default))
                throw new PersonalAIAssistant.Memory.Core.Exceptions.DomainException($"{name} must not be the default value.");
        }

        public static void NotEmpty<T>(IEnumerable<T> items, string name)
        {
            if (items == null || !items.Any()) throw new PersonalAIAssistant.Memory.Core.Exceptions.DomainException($"{name} must contain at least one item.");
        }
    }
}
