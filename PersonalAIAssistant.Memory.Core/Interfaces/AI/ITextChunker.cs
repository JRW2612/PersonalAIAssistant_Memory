namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public record TextChunk(string Text, int Index, int CharacterOffset);
    public record ChunkOptions(int MaxTokens = 500, int OverlapTokens = 50);

    public interface ITextChunker
    {
        IReadOnlyList<TextChunk> Chunk(string text, ChunkOptions options);
    }
}
