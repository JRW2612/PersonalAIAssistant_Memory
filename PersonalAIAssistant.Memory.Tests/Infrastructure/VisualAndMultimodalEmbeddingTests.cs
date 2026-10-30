using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using PersonalAIAssistant.Memory.Infrastructure.AI.Gemini;
using PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure
{
    public class VisualAndMultimodalEmbeddingTests
    {
        private readonly OpenAiEmbeddingService _openAiService;
        private readonly GeminiMultimodalEmbeddingService _geminiService;
        private readonly Mock<IUserContext> _userContextMock;

        public VisualAndMultimodalEmbeddingTests()
        {
            var httpFactoryMock = new Mock<IHttpClientFactory>();
            httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient());

            _userContextMock = new Mock<IUserContext>();
            _userContextMock.Setup(u => u.UserId).Returns("test-user-multimodal");

            var openAiOptions = Options.Create(new OpenAiOptions());
            var gatewayOptions = Options.Create(new AiGatewayOptions { Enabled = false });

            _openAiService = new OpenAiEmbeddingService(
                httpFactoryMock.Object,
                openAiOptions,
                gatewayOptions,
                _userContextMock.Object,
                NullLogger<OpenAiEmbeddingService>.Instance);

            var aiProviderOpts = Options.Create(new AiProviderOptions
            {
                Gemini = new GeminiOptions { ApiKey = "" }
            });

            _geminiService = new GeminiMultimodalEmbeddingService(
                httpFactoryMock.Object,
                aiProviderOpts,
                _userContextMock.Object,
                NullLogger<GeminiMultimodalEmbeddingService>.Instance);
        }

        [Fact]
        public void VisualContent_FromBase64_ShouldDecodeProperly_AndHandleDataUriPrefix()
        {
            var rawBytes = new byte[] { 10, 20, 30, 40, 50 };
            var base64 = Convert.ToBase64String(rawBytes);
            var dataUri = $"data:image/png;base64,{base64}";

            var visual = VisualContent.FromBase64(dataUri, "image/png", "Whiteboard Architecture", VisualModality.WhiteboardDrawing);

            visual.Data.Should().BeEquivalentTo(rawBytes);
            visual.MimeType.Should().Be("image/png");
            visual.Caption.Should().Be("Whiteboard Architecture");
            visual.Modality.Should().Be(VisualModality.WhiteboardDrawing);
        }

        [Fact]
        public async Task OpenAi_GenerateVisualEmbedding_ShouldProduceUnitNormalizedVector()
        {
            var visual = new VisualContent(
                new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                "image/jpeg",
                "Server Room Snapshot",
                VisualModality.CameraSnapshot);

            var result = await _openAiService.GenerateVisualEmbeddingAsync(visual, CancellationToken.None);

            result.Should().NotBeNull();
            result.Vector.Should().HaveCount(1536);
            result.Provider.Should().NotBeNullOrWhiteSpace();

            // Calculate magnitude: sqrt(sum(v_i^2)) should be approximately 1.0
            float sumSq = result.Vector.Sum(x => x * x);
            float magnitude = MathF.Sqrt(sumSq);
            magnitude.Should().BeApproximately(1.0f, 0.01f);
        }

        [Fact]
        public async Task OpenAi_GenerateMultimodalEmbedding_ShouldFuseTextAndVisualVectors()
        {
            var visual = new VisualContent(
                new byte[] { 100, 101, 102 },
                "image/png",
                "Database ERD Sketch",
                VisualModality.WhiteboardDrawing);

            var req = new MultimodalEmbeddingRequest("PostgreSQL Schema Architecture", visual, textWeight: 0.6, visualWeight: 0.4);
            var result = await _openAiService.GenerateMultimodalEmbeddingAsync(req, CancellationToken.None);

            result.Should().NotBeNull();
            result.Vector.Should().HaveCount(1536);

            float sumSq = result.Vector.Sum(x => x * x);
            float magnitude = MathF.Sqrt(sumSq);
            magnitude.Should().BeApproximately(1.0f, 0.01f);
        }

        [Fact]
        public async Task Gemini_GenerateMultimodalEmbedding_ShouldProduceNormalizedVector()
        {
            var visual = new VisualContent(
                new byte[] { 42, 43, 44, 45 },
                "image/webp",
                "Hardware Rack Inspection",
                VisualModality.CameraSnapshot);

            var req = new MultimodalEmbeddingRequest("Checking port wiring", visual);
            var result = await _geminiService.GenerateMultimodalEmbeddingAsync(req, CancellationToken.None);

            result.Should().NotBeNull();
            result.Vector.Should().HaveCount(768);

            float sumSq = result.Vector.Sum(x => x * x);
            float magnitude = MathF.Sqrt(sumSq);
            magnitude.Should().BeApproximately(1.0f, 0.01f);
        }

        [Fact]
        public async Task MultimodalEmbeddingRouter_ShouldRouteBasedOnUserContextPreference()
        {
            var routerOpts = Options.Create(new AiProviderOptions { Default = "openai" });

            // User preference set to Gemini
            _userContextMock.Setup(u => u.PreferredProvider).Returns("gemini");
            var router = new MultimodalEmbeddingRouter(
                _openAiService,
                _geminiService,
                routerOpts,
                _userContextMock.Object,
                NullLogger<MultimodalEmbeddingRouter>.Instance);

            var visual = new VisualContent(new byte[] { 1, 2, 3 }, "image/jpeg", "Test", VisualModality.Diagram);
            var geminiResult = await router.GenerateVisualEmbeddingAsync(visual, CancellationToken.None);

            geminiResult.Vector.Should().HaveCount(768);

            // User preference set to OpenAI
            _userContextMock.Setup(u => u.PreferredProvider).Returns("openai");
            var openAiResult = await router.GenerateVisualEmbeddingAsync(visual, CancellationToken.None);
            openAiResult.Vector.Should().HaveCount(1536);
        }
    }
}
