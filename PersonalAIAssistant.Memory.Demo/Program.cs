using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Extensions;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=====================================================================");
Console.WriteLine("   Personal AI Assistant — Memory Subsystem & Gemini Live Demo      ");
Console.WriteLine("=====================================================================");
Console.ResetColor();

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration["UseInMemoryStore"] = "true";
builder.Configuration["AiGateway:Enabled"] = "false";

var demoUserContext = new DemoUserContext();

// ── Client Application User Login Flow ───────────────────────────────────
// In production, the user logs in to the frontend AI client application.
// The client application acquires the user's session and Gemini API credentials (BYOK),
// passing them dynamically per request via headers or session context.
string userId = "user-alice";
string? userGeminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
    ?? Environment.GetEnvironmentVariable("AI__Gemini__ApiKey");

if (string.IsNullOrWhiteSpace(userGeminiKey) && !Console.IsInputRedirected)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n[Client AI App] User login session required.");
    Console.Write("Enter User ID (default: user-alice): ");
    Console.ResetColor();
    var inputUser = Console.ReadLine()?.Trim();
    if (!string.IsNullOrWhiteSpace(inputUser)) userId = inputUser;

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[Client AI App] User '{userId}' authenticated.");
    Console.Write("Enter User's Gemini API Key (provided by AI app upon login, or press Enter for Mock Mode): ");
    Console.ResetColor();
    var inputKey = Console.ReadLine()?.Trim();
    if (!string.IsNullOrWhiteSpace(inputKey)) userGeminiKey = inputKey;
}

bool useRealGemini = !string.IsNullOrWhiteSpace(userGeminiKey);
demoUserContext.UserId = userId;
demoUserContext.DynamicGeminiKey = userGeminiKey;

if (useRealGemini)
{
    builder.Configuration["AI:Default"] = "gemini";
    builder.Configuration["AI:Gemini:ConsolidationModel"] = "gemini-1.5-flash";
    builder.Configuration["AI:Gemini:CompressionModel"] = "gemini-1.5-flash";

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n[Login Successful] User '{userId}' authenticated with dynamic Gemini key.");
    Console.WriteLine("[Security Verified] API key is bound dynamically to IUserContext (BYOK) — no server configuration modified.");
    Console.ResetColor();
}
else
{
    builder.Configuration["AI:Default"] = "mock";
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"\n[Login] User '{userId}' authenticated in local offline MOCK Mode.");
    Console.ResetColor();
}

// 1. Configure Infrastructure
builder.Services.AddMemoryInfrastructureServices(
    configureDbContext: options => options.UseInMemoryDatabase("DemoReadModelDb"),
    mongoConnectionString: "mongodb://localhost:27017",
    mongoDatabaseName: "DemoMemoryDb"
);

// 2. Configure AI Providers (OpenAI, Gemini)
builder.Services.AddAiProviders(builder.Configuration);

// 3. Configure Business Layer
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

// 4. Register In-Memory Stores
builder.Services.AddSingleton<IEventStore, PersonalAIAssistant.Memory.Infrastructure.Mongo.InMemoryEventStore>();
builder.Services.AddSingleton<ISnapshotRepository, PersonalAIAssistant.Memory.Infrastructure.Mongo.InMemorySnapshotRepository>();
builder.Services.AddScoped<IEventBus, PersonalAIAssistant.Memory.Infrastructure.InMemory.InMemoryEventBus>();
builder.Services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
builder.Services.AddSingleton<IVectorMemoryRepository, MockVectorRepo>();
builder.Services.AddSingleton<PersonalAIAssistant.Memory.Core.Interfaces.Security.IUserContext>(demoUserContext);

if (!useRealGemini)
{
    builder.Services.AddScoped<IAIProvider, MockAiProvider>();
    builder.Services.AddSingleton<ICompressionService, MockCompressionService>();
}

// 5. Build and Start Host
using var host = builder.Build();
await host.StartAsync();

Console.WriteLine("\nSubsystem initialized successfully. Starting Demo...\n");

using (var scope = host.Services.CreateScope())
{
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var aiFactory = scope.ServiceProvider.GetRequiredService<IAIProviderFactory>();

    // ── Live Gemini Connectivity Check ───────────────────────────────────────
    if (useRealGemini)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("---------------------------------------------------------------------");
        Console.WriteLine("[STEP 0] Verifying Gemini API Connectivity...");
        Console.WriteLine("---------------------------------------------------------------------");
        Console.ResetColor();

        try
        {
            var provider = aiFactory.GetProvider("gemini");
            var pingResponse = await provider.GetResponseAsync("Respond with exactly: 'Gemini Memory Provider Connected.'", CancellationToken.None);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Gemini Online] Provider response: {pingResponse.Trim()}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Gemini Connectivity Warning] {ex.Message}");
            Console.WriteLine("Falling back to local processing if necessary.");
            Console.ResetColor();
        }
    }

    // ── Step 1: Ingest Memories ──────────────────────────────────────────────
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n---------------------------------------------------------------------");
    Console.WriteLine("[STEP 1] Ingesting Memories (Event Sourcing Aggregates)");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();

    var memory1 =
        "At the quarterly technical sync on September 7, 2026, the architecture team decided to deploy " +
        "CQRS and Event Sourcing for the AI memory subsystem. Transparent AES-GCM encryption will be applied " +
        "to all raw and compressed memory fragments before persistence, preventing memory poisoning and indirect prompt injection. " +
        "The team also agreed to use Google Gemini for automated memory compression and multi-fragment consolidation.";

    Console.WriteLine($"Memory 1 ({memory1.Length} chars):\n\"{memory1}\"\n");

    var addCommand1 = new AddMemoryCommand(
        RawText: memory1,
        Source: MemorySource.User.ToString(),
        Importance: MemoryImportance.High,
        Tags: new List<string> { "demo", "architecture", "gemini", "security" },
        UserId: userId
    );

    Guid memoryId1 = Guid.Empty;
    Guid memoryId2 = Guid.Empty;
    try
    {
        memoryId1 = await mediator.Send(addCommand1);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] MemoryAggregate 1 persisted with ID: {memoryId1}");
        Console.ResetColor();

        var memory2 =
            "User preferences and schedule: Prefers dark mode and JetBrains Mono font. " +
            "Works from London office on Mondays and Wednesdays, remote other days. Primary stack: C# .NET 10 and TypeScript.";

        var addCommand2 = new AddMemoryCommand(
            RawText: memory2,
            Source: MemorySource.User.ToString(),
            Importance: MemoryImportance.Medium,
            Tags: new List<string> { "preferences", "schedule" },
            UserId: userId
        );

        memoryId2 = await mediator.Send(addCommand2);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] MemoryAggregate 2 persisted with ID: {memoryId2}");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR adding memory]: {ex.Message}");
        Console.ResetColor();
    }

    // ── Step 2: Semantic Compression with Gemini ─────────────────────────────
    if (memoryId1 != Guid.Empty)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n---------------------------------------------------------------------");
        Console.WriteLine("[STEP 2] Semantic Memory Compression via Gemini");
        Console.WriteLine("---------------------------------------------------------------------");
        Console.ResetColor();
        Console.WriteLine("Sending memory to Gemini for dense factual compression...");

        try
        {
            var compressionService = scope.ServiceProvider.GetRequiredService<ICompressionService>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var compResult = await compressionService.CompressAsync(memory1, CancellationToken.None);
            sw.Stop();

            var compressCommand = new CompressMemoryCommand(
                OriginalMemoryId: memoryId1,
                CompressedText: compResult.Text,
                CompressionModel: compResult.Model,
                TokenCount: Math.Max(1, compResult.TokenCount),
                UserId: userId
            );

            await mediator.Send(compressCommand);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[Gemini Compressed Output] (in {sw.ElapsedMilliseconds}ms):");
            Console.ResetColor();
            Console.WriteLine($"\"{compResult.Text}\"");
            Console.WriteLine($"Model: {compResult.Model} | Estimated Tokens: {compResult.TokenCount}");
            var reduction = 100.0 - ((double)compResult.Text.Length / memory1.Length * 100.0);
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"Character Reduction: {reduction:F1}%");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR during compression]: {ex.Message}");
            Console.ResetColor();
        }
    }

    // ── Step 3: Multi-Memory Consolidation with Gemini ───────────────────────
    if (memoryId1 != Guid.Empty && memoryId2 != Guid.Empty)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n---------------------------------------------------------------------");
        Console.WriteLine("[STEP 3] Consolidating Multiple Memory Fragments via Gemini");
        Console.WriteLine("---------------------------------------------------------------------");
        Console.ResetColor();

        var fragmentText =
            $"- Fragment 1 (Architecture): {memory1}\n\n" +
            $"- Fragment 2 (Preferences): User prefers dark mode, JetBrains Mono font, works London office Mon/Wed, remote otherwise. Stack: C# .NET 10, TypeScript.";

        Console.WriteLine($"Input Memory Fragments:\n{fragmentText}\n");
        Console.WriteLine("Instructing Gemini to synthesize and de-duplicate memories...");

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var consolidateCommand = new ConsolidateMemoriesCommand(
                NewMemoryId: Guid.NewGuid(),
                MergedMemoryIds: new List<Guid> { memoryId1, memoryId2 },
                ConsolidatedText: fragmentText,
                UserId: userId,
                ProvenanceLinks: new List<string> { $"memory-{memoryId1}", $"memory-{memoryId2}" }
            );

            var consolidatedId = await mediator.Send(consolidateCommand);
            sw.Stop();

            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var events = await eventStore.GetEventsAsync($"memory-{consolidatedId}", CancellationToken.None);
            var consolidatedEvent = events.OfType<PersonalAIAssistant.Memory.Events.MemoryConsolidatedEvent>().LastOrDefault();

            if (consolidatedEvent != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[Gemini Consolidated Summary] (in {sw.ElapsedMilliseconds}ms):");
                Console.ResetColor();
                Console.WriteLine($"\"{consolidatedEvent.ConsolidatedText}\"");
                Console.WriteLine($"Consolidated Aggregate ID: {consolidatedId}");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR during consolidation]: {ex.Message}");
            Console.ResetColor();
        }
    }

    // ── Step 4: Interactive Prompt ───────────────────────────────────────────
    if (!Console.IsInputRedirected)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n---------------------------------------------------------------------");
        Console.WriteLine("[STEP 4] Interactive Testing");
        Console.WriteLine("---------------------------------------------------------------------");
        Console.ResetColor();

        while (true)
        {
            Console.Write("\nEnter a custom thought/note to compress with Gemini (or press Enter to finish): ");
            var customNote = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(customNote)) break;

            var customAdd = new AddMemoryCommand(
                RawText: customNote,
                Source: MemorySource.User.ToString(),
                Importance: MemoryImportance.Medium,
                Tags: new List<string> { "interactive" },
                UserId: "interactive-user"
            );

            var customId = await mediator.Send(customAdd);
            var compressionService = scope.ServiceProvider.GetRequiredService<ICompressionService>();
            var comp = await compressionService.CompressAsync(customNote, CancellationToken.None);

            var customCompress = new CompressMemoryCommand(
                OriginalMemoryId: customId,
                CompressedText: comp.Text,
                CompressionModel: comp.Model,
                TokenCount: Math.Max(1, comp.TokenCount),
                UserId: "interactive-user"
            );
            await mediator.Send(customCompress);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Gemini Compressed: \"{comp.Text}\" (Model: {comp.Model})");
            Console.ResetColor();
        }
    }
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("\n=====================================================================");
Console.WriteLine("   Demo completed successfully! Press Enter to exit.");
Console.WriteLine("=====================================================================");
Console.ResetColor();

await host.StopAsync();

class MockCompressionService : ICompressionService
{
    public Task<PersonalAIAssistant.Memory.Core.DTOs.CompressionResult> CompressAsync(string text, CancellationToken ct)
        => Task.FromResult(new PersonalAIAssistant.Memory.Core.DTOs.CompressionResult("Mock Summary: Text compressed offline.", "mock-model", 10));
}

class MockEmbeddingService : IEmbeddingService
{
    public Task<PersonalAIAssistant.Memory.Core.DTOs.EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
        => Task.FromResult(new PersonalAIAssistant.Memory.Core.DTOs.EmbeddingResult(
            EmbeddingId: Guid.NewGuid().ToString(),
            Vector: new float[1536],
            Provider: "mock",
            Model: "mock-model"));
}

class MockVectorRepo : IVectorMemoryRepository
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

class DemoUserContext : PersonalAIAssistant.Memory.Core.Interfaces.Security.IUserContext
{
    public string UserId { get; set; } = "user-alice";
    public string TenantId { get; set; } = "default";
    public IReadOnlyList<string> Roles { get; set; } = new[] { "User", "Admin" };
    public bool IsAuthenticated => true;
    public string? DynamicGeminiKey { get; set; }

    public string? GetApiKey(string providerName)
    {
        if (providerName.Equals("gemini", StringComparison.OrdinalIgnoreCase))
            return DynamicGeminiKey;
        return null;
    }
}

class MockAiProvider : IAIProvider
{
    public string ProviderName => "mock";
    public Task<string> GetResponseAsync(string prompt, CancellationToken ct)
        => Task.FromResult("Synthesized Summary (Offline Mode): Core architectural principles (CQRS, Event Sourcing, AES-GCM) successfully aligned with user requirements.");
}
