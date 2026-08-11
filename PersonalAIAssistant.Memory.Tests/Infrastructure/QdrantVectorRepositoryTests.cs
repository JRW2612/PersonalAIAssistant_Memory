using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using Qdrant.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure
{
    public class QdrantVectorRepositoryTests
    {
        [Fact]
        public void VectorSearchResult_DTO_Properties_Should_Initialize_Correctly()
        {
            var memoryId = Guid.NewGuid();
            var result = new Core.DTOs.VectorSearchResult(memoryId, "emb-123", 0.95f);

            result.MemoryId.Should().Be(memoryId);
            result.EmbeddingId.Should().Be("emb-123");
            result.Score.Should().Be(0.95f);
        }
    }
}
