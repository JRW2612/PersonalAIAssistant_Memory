using System.Collections.Generic;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface ITextChunker
    {
        IReadOnlyList<TextChunk> Chunk(string text, ChunkOptions options);
    }

    public record ChunkOptions(int MaxTokens = 512, int OverlapTokens = 64);
    
    public record TextChunk(string Text, int Index, int TokenCount);
}
