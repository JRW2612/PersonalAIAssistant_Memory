using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure
{
    public class EmbeddedEdgeVectorMemoryRepositoryTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly EmbeddedEdgeVectorMemoryRepository _repo;

        public EmbeddedEdgeVectorMemoryRepositoryTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "edge_vectors_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            var options = Options.Create(new QdrantOptions
            {
                Mode = "LocalEdge",
                LocalEdgeProvider = "EmbeddedInProcess",
                LocalStoragePath = _tempDir,
                CollectionName = "test_memories"
            });

            _repo = new EmbeddedEdgeVectorMemoryRepository(options, NullLogger<EmbeddedEdgeVectorMemoryRepository>.Instance);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task UpsertAndSearch_ShouldReturnTopKMatchingVectors_RankedByCosineSimilarity()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            // Vector 1 is identical to query direction
            var vec1 = new float[] { 1.0f, 0.0f, 0.0f };
            // Vector 2 is orthogonal to query direction
            var vec2 = new float[] { 0.0f, 1.0f, 0.0f };
            // Vector 3 is 45 degrees
            var vec3 = new float[] { 0.707f, 0.707f, 0.0f };

            await _repo.UpsertAsync(id1, "emb-1", vec1, "user-1", CancellationToken.None);
            await _repo.UpsertAsync(id2, "emb-2", vec2, "user-1", CancellationToken.None);
            await _repo.UpsertAsync(id3, "emb-3", vec3, "user-1", CancellationToken.None);

            var query = new float[] { 1.0f, 0.0f, 0.0f };
            var results = await _repo.SearchAsync(query, topK: 2, userId: "user-1", CancellationToken.None);

            results.Should().HaveCount(2);
            results[0].MemoryId.Should().Be(id1);
            results[0].Score.Should().BeApproximately(1.0f, 0.01f);
            results[1].MemoryId.Should().Be(id3);
            results[1].Score.Should().BeApproximately(0.707f, 0.01f);
        }

        [Fact]
        public async Task Search_ShouldIsolateByUserId_WhenFilteringIsApplied()
        {
            var idUserA = Guid.NewGuid();
            var idUserB = Guid.NewGuid();
            var vec = new float[] { 1.0f, 1.0f, 1.0f };

            await _repo.UpsertAsync(idUserA, "emb-a", vec, "user-a", CancellationToken.None);
            await _repo.UpsertAsync(idUserB, "emb-b", vec, "user-b", CancellationToken.None);

            var resultsA = await _repo.SearchAsync(vec, topK: 5, userId: "user-a", CancellationToken.None);
            var resultsB = await _repo.SearchAsync(vec, topK: 5, userId: "user-b", CancellationToken.None);

            resultsA.Should().ContainSingle(r => r.MemoryId == idUserA);
            resultsA.Should().NotContain(r => r.MemoryId == idUserB);

            resultsB.Should().ContainSingle(r => r.MemoryId == idUserB);
            resultsB.Should().NotContain(r => r.MemoryId == idUserA);
        }

        [Fact]
        public async Task Delete_ShouldRemoveVector_FromSubsequentSearches()
        {
            var id = Guid.NewGuid();
            var vec = new float[] { 0.5f, 0.5f, 0.5f };

            await _repo.UpsertAsync(id, "emb-del", vec, "user-1", CancellationToken.None);
            var initial = await _repo.SearchAsync(vec, topK: 1, userId: "user-1", CancellationToken.None);
            initial.Should().HaveCount(1);

            await _repo.DeleteAsync(id, CancellationToken.None);
            var after = await _repo.SearchAsync(vec, topK: 1, userId: "user-1", CancellationToken.None);
            after.Should().BeEmpty();
        }

        [Fact]
        public async Task Persistence_ShouldReloadVectorsFromDisk_OnFreshRepositoryInstance()
        {
            var id = Guid.NewGuid();
            var vec = new float[] { 0.6f, 0.8f, 0.0f };

            await _repo.UpsertAsync(id, "emb-persist", vec, "user-offline", CancellationToken.None);

            // Recreate repository pointing to the same disk path
            var options = Options.Create(new QdrantOptions
            {
                Mode = "LocalEdge",
                LocalStoragePath = _tempDir,
                CollectionName = "test_memories"
            });
            var newRepoInstance = new EmbeddedEdgeVectorMemoryRepository(options, NullLogger<EmbeddedEdgeVectorMemoryRepository>.Instance);

            var results = await newRepoInstance.SearchAsync(vec, topK: 1, userId: "user-offline", CancellationToken.None);
            results.Should().ContainSingle();
            results[0].MemoryId.Should().Be(id);
            results[0].EmbeddingId.Should().Be("emb-persist");
        }
    }
}
