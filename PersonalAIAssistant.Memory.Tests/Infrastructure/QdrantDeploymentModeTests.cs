using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using Qdrant.Client;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure
{
    public class QdrantDeploymentModeTests : IDisposable
    {
        private readonly string _tempDir;

        public QdrantDeploymentModeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "qdrant_mode_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
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
        public void QdrantOptions_ModeProperties_ShouldReflectCloudAndLocalEdge()
        {
            var edgeOpts = new QdrantOptions { Mode = "LocalEdge" };
            edgeOpts.IsLocalEdgeMode.Should().BeTrue();
            edgeOpts.IsCloudMode.Should().BeFalse();

            var cloudOpts = new QdrantOptions { Mode = "Cloud" };
            cloudOpts.IsCloudMode.Should().BeTrue();
            cloudOpts.IsLocalEdgeMode.Should().BeFalse();
        }

        [Fact]
        public async Task QdrantVectorRepository_InLocalEdgeMode_ShouldFallbackToEmbeddedStore_WhenContainerIsOffline()
        {
            var edgeOpts = Options.Create(new QdrantOptions
            {
                Mode = "LocalEdge",
                LocalEdgeProvider = "Auto",
                LocalStoragePath = _tempDir,
                CollectionName = "offline_resilience_test",
                Host = "127.0.0.1",
                Port = 64999 // Unused port so container connection fails immediately
            });

            var edgeFallback = new EmbeddedEdgeVectorMemoryRepository(
                edgeOpts,
                NullLogger<EmbeddedEdgeVectorMemoryRepository>.Instance);

            var dummyClient = new QdrantClient(host: "127.0.0.1", port: 64999);

            var hybridRepo = new QdrantVectorRepository(
                dummyClient,
                edgeOpts,
                edgeFallback,
                NullLogger<QdrantVectorRepository>.Instance);

            var memoryId = Guid.NewGuid();
            var vector = new float[] { 0.1f, 0.2f, 0.3f };

            // Upsert should not throw even if Qdrant container is unreachable
            Func<Task> actUpsert = async () =>
                await hybridRepo.UpsertAsync(memoryId, "emb-offline", vector, "user-offline", CancellationToken.None);
            await actUpsert.Should().NotThrowAsync();

            // Search should successfully query the embedded edge fallback
            var results = await hybridRepo.SearchAsync(vector, topK: 1, userId: "user-offline", CancellationToken.None);
            results.Should().ContainSingle();
            results[0].MemoryId.Should().Be(memoryId);

            // Delete should also succeed against the edge store
            Func<Task> actDelete = async () =>
                await hybridRepo.DeleteAsync(memoryId, CancellationToken.None);
            await actDelete.Should().NotThrowAsync();

            var afterDelete = await hybridRepo.SearchAsync(vector, topK: 1, userId: "user-offline", CancellationToken.None);
            afterDelete.Should().BeEmpty();
        }
    }
}
