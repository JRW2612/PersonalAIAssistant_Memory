using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Business.Extensions;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;

Console.WriteLine("Initializing Personal AI Assistant - Memory Subsystem Demo...");

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Infrastructure
builder.Services.AddMemoryInfrastructureServices(
    configureDbContext: options => options.UseInMemoryDatabase("DemoReadModelDb"),
    mongoConnectionString: "mongodb://localhost:27017",
    mongoDatabaseName: "DemoMemoryDb"
);

// 2. Configure AI Providers (OpenAI, Gemini) and Teams Webhook
//    Keys are read from appsettings.json or user-secrets:
//      dotnet user-secrets set "AI:OpenAi:ApiKey"  "sk-..."
//      dotnet user-secrets set "AI:Gemini:ApiKey"  "AIza..."
//      dotnet user-secrets set "Teams:WebhookUrl"  "https://outlook.office.com/webhook/..."
builder.Services.AddAiProviders(builder.Configuration);

// 3. Configure Business Layer (including Polly Resilience Pipelines and Workers)
builder.Services.AddMemoryBusinessServices(
    configureConsolidation: opts => 
    {
        opts.BatchSize = 5;
        opts.PollInterval = TimeSpan.FromSeconds(10);
    },
    configureSnapshot: opts => 
    {
        opts.BatchSize = 10;
        opts.PollInterval = TimeSpan.FromSeconds(30);
    }
);

// 4. Register Mock Services for Demo
//    Replace with real implementations when deploying.
builder.Services.AddSingleton<PersonalAIAssistant.Memory.Core.Interfaces.Others.ICompressionService, MockCompressionService>();
builder.Services.AddSingleton<PersonalAIAssistant.Memory.Core.Interfaces.Others.IEmbeddingService, MockEmbeddingService>();
builder.Services.AddSingleton<PersonalAIAssistant.Memory.Core.Interfaces.Others.IVectorMemoryRepository, MockVectorRepo>();

// 5. Build and Run the Host
using var host = builder.Build();

Console.WriteLine("Host built successfully. Starting workers...");

// Demo Execution
_ = Task.Run(async () =>
{
    // Wait briefly for host to settle
    await Task.Delay(2000);
    
    using var scope = host.Services.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

    Console.WriteLine("\n[DEMO] Dispatching an AddMemoryCommand...");
    var addCommand = new AddMemoryCommand(
        RawText: "I learned how to implement Event Sourcing with MongoDB today.",
        Source: MemorySource.User.ToString(),
        Importance: MemoryImportance.High,
        Tags: new List<string> { "demo", "architecture" },
        UserId: "user-123"
    );

    try
    {
        var memoryId = await mediator.Send(addCommand);
        Console.WriteLine($"[DEMO] Successfully added memory! Aggregate ID: {memoryId}");
        
        Console.WriteLine("\n[DEMO] Dispatching a DeleteMemoryCommand (Authorized Request)...");
        var deleteCommand = new DeleteMemoryCommand(memoryId, "Demo Cleanup", "user-123");
        
        // This will pass through the new AuthorizationBehavior
        await mediator.Send(deleteCommand);
        Console.WriteLine("[DEMO] Successfully processed Delete request. Authorization passed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEMO ERROR] {ex.Message}");
    }
});

Console.WriteLine("Press Ctrl+C to shut down.");
await host.RunAsync();

class MockCompressionService : PersonalAIAssistant.Memory.Core.Interfaces.Others.ICompressionService
{
    public Task<PersonalAIAssistant.Memory.Core.DTOs.CompressionResult> CompressAsync(string text, CancellationToken ct)
        => Task.FromResult(new PersonalAIAssistant.Memory.Core.DTOs.CompressionResult("Mock Summary", "mock-model", 10));
}

class MockEmbeddingService : PersonalAIAssistant.Memory.Core.Interfaces.Others.IEmbeddingService
{
    public Task<PersonalAIAssistant.Memory.Core.DTOs.EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
        => Task.FromResult(new PersonalAIAssistant.Memory.Core.DTOs.EmbeddingResult(
            EmbeddingId: Guid.NewGuid().ToString(),
            Vector:      new float[1536],
            Provider:    "mock",
            Model:       "mock-model"));
}

class MockVectorRepo : PersonalAIAssistant.Memory.Core.Interfaces.Others.IVectorMemoryRepository
{
    public Task UpsertAsync(Guid memoryId, string text, IReadOnlyList<float> vector, string? userId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<IReadOnlyList<PersonalAIAssistant.Memory.Core.DTOs.VectorSearchResult>> SearchAsync(
        IReadOnlyList<float> vector, int limit, string? userId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PersonalAIAssistant.Memory.Core.DTOs.VectorSearchResult>>(
            Array.Empty<PersonalAIAssistant.Memory.Core.DTOs.VectorSearchResult>());

    public Task DeleteAsync(Guid memoryId, CancellationToken ct)
        => Task.CompletedTask;
}
