using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;
using PersonalAIAssistant.Memory.Core.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Controllers
{
    public class MemoriesControllerTests
    {
        private readonly Mock<IMediator> _mediatorMock;
        private readonly Mock<IReadModelRepository> _readRepoMock;
        private readonly MemoriesController _controller;

        public MemoriesControllerTests()
        {
            _mediatorMock = new Mock<IMediator>();
            _readRepoMock = new Mock<IReadModelRepository>();
            _controller = new MemoriesController(_mediatorMock.Object, _readRepoMock.Object);

            var httpContext = new DefaultHttpContext();
            httpContext.Items["UserId"] = "test-user-123";
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        [Fact]
        public async Task AddMemory_ValidDto_ReturnsCreatedAtAction_WithCreateMemoryResponseDto()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var dto = new CreateMemoryDto(
                RawText: "Remembering important architectural decisions.",
                Source: "User",
                Importance: MemoryImportance.High,
                Tags: new List<string> { "architecture", "csharp" },
                CorrelationId: "corr-001"
            );

            _mediatorMock
                .Setup(m => m.Send(It.Is<AddMemoryCommand>(c =>
                    c.RawText == dto.RawText &&
                    c.UserId == "test-user-123" &&
                    c.Source == "User" &&
                    c.Importance == MemoryImportance.High &&
                    c.CorrelationId == "corr-001"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(memoryId);

            // Act
            var result = await _controller.AddMemory(dto, CancellationToken.None);

            // Assert
            var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
            createdResult.ActionName.Should().Be(nameof(MemoriesController.GetMemoryById));
            createdResult.Value.Should().BeOfType<CreateMemoryResponseDto>();
            var responseDto = (CreateMemoryResponseDto)createdResult.Value!;
            responseDto.MemoryId.Should().Be(memoryId);
        }

        [Fact]
        public async Task SearchMemories_ValidQuery_ReturnsOk_WithFusedMemoryPrompt()
        {
            // Arrange
            var fusedPrompt = new FusedMemoryPrompt(
                Query: "architectural decisions",
                ContextBlock: "Context...",
                RetrievedMemories: new List<MemoryReadModel>()
            );

            _mediatorMock
                .Setup(m => m.Send(It.Is<RetrieveMemoriesQuery>(q =>
                    q.UserId == "test-user-123" &&
                    q.QueryText == "architectural decisions" &&
                    q.TopK == 5), It.IsAny<CancellationToken>()))
                .ReturnsAsync(fusedPrompt);

            // Act
            var result = await _controller.SearchMemories("architectural decisions", 5, CancellationToken.None);

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().Be(fusedPrompt);
        }

        [Fact]
        public async Task GetMemoryById_WhenFound_ReturnsOk_WithMemoryReadModel()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var memoryModel = new MemoryReadModel
            {
                Id = memoryId,
                UserId = "test-user-123",
                RawText = "Found memory",
                Status = MemoryStatus.Active.ToString()
            };

            _readRepoMock
                .Setup(r => r.GetMemoriesByIdsAsync(It.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == memoryId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel> { memoryModel });

            // Act
            var result = await _controller.GetMemoryById(memoryId, CancellationToken.None);

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().Be(memoryModel);
        }

        [Fact]
        public async Task GetMemoryById_WhenNotFound_ReturnsNotFound()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            _readRepoMock
                .Setup(r => r.GetMemoriesByIdsAsync(It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel>());

            // Act
            var result = await _controller.GetMemoryById(memoryId, CancellationToken.None);

            // Assert
            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task UpdateMemory_ValidDto_ReturnsNoContent()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var dto = new UpdateMemoryDto("Updated raw text content");

            _mediatorMock
                .Setup(m => m.Send(It.Is<UpdateMemoryCommand>(c =>
                    c.MemoryId == memoryId &&
                    c.UserId == "test-user-123" &&
                    c.UpdatedFields != null &&
                    c.UpdatedFields["RawText"] == "Updated raw text content"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(memoryId);

            // Act
            var result = await _controller.UpdateMemory(memoryId, dto, CancellationToken.None);

            // Assert
            result.Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task DeleteMemory_ReturnsNoContent()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            _mediatorMock
                .Setup(m => m.Send(It.Is<DeleteMemoryCommand>(c =>
                    c.MemoryId == memoryId &&
                    c.UserId == "test-user-123" &&
                    c.Reason == "User requested deletion"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(memoryId);

            // Act
            var result = await _controller.DeleteMemory(memoryId, null, CancellationToken.None);

            // Assert
            result.Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task CompressMemory_ValidDto_ReturnsOk_WithCompressMemoryResponseDto()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var dto = new CompressRequestDto("Compressed summary", "gpt-4o-mini", 42);

            _mediatorMock
                .Setup(m => m.Send(It.Is<CompressMemoryCommand>(c =>
                    c.OriginalMemoryId == memoryId &&
                    c.CompressedText == dto.CompressedText &&
                    c.CompressionModel == "gpt-4o-mini" &&
                    c.TokenCount == 42 &&
                    c.UserId == "test-user-123"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(memoryId);

            // Act
            var result = await _controller.CompressMemory(memoryId, dto, CancellationToken.None);

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeOfType<CompressMemoryResponseDto>();
            var responseDto = (CompressMemoryResponseDto)okResult.Value!;
            responseDto.MemoryId.Should().Be(memoryId);
            responseDto.Status.Should().Be("Compressed");
        }

        [Fact]
        public async Task ConsolidateMemories_ValidDto_ReturnsOk_WithConsolidateMemoriesResponseDto()
        {
            // Arrange
            var newId = Guid.NewGuid();
            var sourceIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            var dto = new ConsolidateRequestDto(
                MergedMemoryIds: sourceIds,
                ConsolidatedText: "Unified summary across multiple items.",
                ProvenanceLinks: new List<string> { "link-1", "link-2" }
            );

            _mediatorMock
                .Setup(m => m.Send(It.Is<ConsolidateMemoriesCommand>(c =>
                    c.MergedMemoryIds == sourceIds &&
                    c.ConsolidatedText == dto.ConsolidatedText &&
                    c.UserId == "test-user-123"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(newId);

            // Act
            var result = await _controller.ConsolidateMemories(dto, CancellationToken.None);

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeOfType<ConsolidateMemoriesResponseDto>();
            var responseDto = (ConsolidateMemoriesResponseDto)okResult.Value!;
            responseDto.NewMemoryId.Should().Be(newId);
            responseDto.Status.Should().Be("Consolidated");
        }
    }
}
