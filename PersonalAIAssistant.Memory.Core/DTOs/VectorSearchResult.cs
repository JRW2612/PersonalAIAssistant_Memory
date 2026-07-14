using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Core.DTOs
{
    /// <summary>
    /// A single match returned from a semantic (vector similarity) search.
    /// </summary>
    public record VectorSearchResult(Guid MemoryId, string EmbeddingId, double Score);
}
