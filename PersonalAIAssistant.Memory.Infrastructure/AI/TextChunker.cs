using PersonalAIAssistant.Memory.Core.Interfaces.AI;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    public class TextChunker : ITextChunker
    {
        public IReadOnlyList<TextChunk> Chunk(string text, ChunkOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.MaxTokens <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxTokens must be greater than zero.");
            if (options.OverlapTokens < 0 || options.OverlapTokens >= options.MaxTokens)
                throw new ArgumentOutOfRangeException(nameof(options), "OverlapTokens must be non-negative and less than MaxTokens.");

            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<TextChunk>();
            }

            var chunks = new List<TextChunk>();
            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= options.MaxTokens)
            {
                chunks.Add(new TextChunk(text, 0, words.Length));
                return chunks;
            }

            int index = 0;
            int currentTokenIndex = 0;

            while (currentTokenIndex < words.Length)
            {
                int remaining = words.Length - currentTokenIndex;
                int take = Math.Min(remaining, options.MaxTokens);

                var chunkWords = new string[take];
                Array.Copy(words, currentTokenIndex, chunkWords, 0, take);

                string chunkText = string.Join(" ", chunkWords);
                chunks.Add(new TextChunk(chunkText, index++, take));

                currentTokenIndex += (options.MaxTokens - options.OverlapTokens);
            }

            return chunks;
        }
    }
}
