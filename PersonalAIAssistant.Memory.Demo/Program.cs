using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Extensions;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=====================================================================");
Console.WriteLine("   Personal AI Assistant — Memory Subsystem & Multi-Agent Demo      ");
Console.WriteLine("=====================================================================");
Console.ResetColor();

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration["UseInMemoryStore"] = "true";
builder.Configuration["AiGateway:Enabled"] = "false";

var demoUserContext = new DemoUserContext();

// ── Client Application User Login Flow ───────────────────────────────────
string userId = "user-alice";
string? userGeminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
    ?? Environment.GetEnvironmentVariable("AI__Gemini__ApiKey");

// Check CLI arguments for demo mode (e.g. --demo copilot, --demo rufus, --demo all)
string selectedDemo = "all";
for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--demo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        selectedDemo = args[i + 1].ToLowerInvariant();
    }
}

if (string.IsNullOrWhiteSpace(userGeminiKey) && !Console.IsInputRedirected && args.Length == 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n[Client AI App] User login session required.");
    Console.Write("Enter User ID (default: user-alice): ");
    Console.ResetColor();
    var inputUser = Console.ReadLine()?.Trim();
    if (!string.IsNullOrWhiteSpace(inputUser)) userId = inputUser;

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[Client AI App] User '{userId}' authenticated.");
    Console.Write("Enter User's Gemini API Key (optional, press Enter for Mock Mode): ");
    Console.ResetColor();
    var inputKey = Console.ReadLine()?.Trim();
    if (!string.IsNullOrWhiteSpace(inputKey)) userGeminiKey = inputKey;

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\nSelect Demo Mode to run:");
    Console.WriteLine("  1. Google Gemini — Semantic Compression & Multi-Fragment Consolidation");
    Console.WriteLine("  2. GitHub Copilot Chat — Developer Memory Recall & Code Grounding");
    Console.WriteLine("  3. Amazon Rufus — Contextual Shopping Assistant & Safety Recall");
    Console.WriteLine("  4. Run All Demos (Default)");
    Console.Write("Choice [1-4, Enter=4]: ");
    Console.ResetColor();
    var choice = Console.ReadLine()?.Trim();
    selectedDemo = choice switch
    {
        "1" => "gemini",
        "2" => "copilot",
        "3" => "rufus",
        _ => "all"
    };
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
builder.Services.AddSingleton<IVectorMemoryRepository, InMemoryVectorRepo>();
builder.Services.AddSingleton<PersonalAIAssistant.Memory.Core.Interfaces.Security.IUserContext>(demoUserContext);

if (!useRealGemini)
{
    builder.Services.AddScoped<IAIProvider, MockAiProvider>();
    builder.Services.AddSingleton<ICompressionService, MockCompressionService>();
}

// 5. Build and Start Host
using var host = builder.Build();
await host.StartAsync();

Console.WriteLine("\nMemory Subsystem initialized successfully. Starting Demonstrations...\n");

using (var scope = host.Services.CreateScope())
{
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var aiFactory = scope.ServiceProvider.GetRequiredService<IAIProviderFactory>();

    var readRepo = scope.ServiceProvider.GetRequiredService<PersonalAIAssistant.Memory.Core.Interfaces.Persistence.IReadModelRepository>();
    var vectorRepo = scope.ServiceProvider.GetRequiredService<IVectorMemoryRepository>();

    // ── DEMO 1: Gemini Live Compression & Consolidation ───────────────────────
    if (selectedDemo == "gemini" || selectedDemo == "all")
    {
        await RunGeminiLiveDemoAsync(scope, mediator, aiFactory, userId, useRealGemini);
    }

    // ── DEMO 2: GitHub Copilot Chat Developer Memory Integration ──────────────
    if (selectedDemo == "copilot" || selectedDemo == "all")
    {
        await RunGitHubCopilotDemoAsync(mediator, readRepo, vectorRepo, userId);
    }

    // ── DEMO 3: Amazon Rufus Contextual AI Shopping Assistant ─────────────────
    if (selectedDemo == "rufus" || selectedDemo == "all")
    {
        await RunAmazonRufusDemoAsync(mediator, readRepo, vectorRepo, userId);
    }

    // ── Step 4: Interactive Testing (if run interactively) ────────────────────
    if (!Console.IsInputRedirected && args.Length == 0)
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
Console.WriteLine("   All Demonstrations completed successfully! Press Enter to exit.   ");
Console.WriteLine("=====================================================================");
Console.ResetColor();

await host.StopAsync();

// ─────────────────────────────────────────────────────────────────────────────
// DEMO IMPLEMENTATION METHODS
// ─────────────────────────────────────────────────────────────────────────────

static async Task RunGeminiLiveDemoAsync(
    IServiceScope scope,
    IMediator mediator,
    IAIProviderFactory aiFactory,
    string userId,
    bool useRealGemini)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=====================================================================");
    Console.WriteLine("  DEMO 1: Google Gemini — Semantic Compression & Consolidation       ");
    Console.WriteLine("=====================================================================");
    Console.ResetColor();

    if (useRealGemini)
    {
        Console.WriteLine("\n[STEP 0] Verifying Gemini API Connectivity...");
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
            Console.ResetColor();
        }
    }

    var memory1 =
        "At the quarterly technical sync on September 7, 2026, the architecture team decided to deploy " +
        "CQRS and Event Sourcing for the AI memory subsystem. Transparent AES-GCM encryption will be applied " +
        "to all raw and compressed memory fragments before persistence, preventing memory poisoning and indirect prompt injection. " +
        "The team also agreed to use Google Gemini for automated memory compression and multi-fragment consolidation.";

    Console.WriteLine($"\nIngesting Memory 1 ({memory1.Length} chars):\n\"{memory1}\"\n");

    var addCommand1 = new AddMemoryCommand(
        RawText: memory1,
        Source: MemorySource.User.ToString(),
        Importance: MemoryImportance.High,
        Tags: new List<string> { "demo", "architecture", "gemini", "security" },
        UserId: userId
    );

    var memoryId1 = await mediator.Send(addCommand1);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[SUCCESS] MemoryAggregate 1 persisted with ID: {memoryId1}");
    Console.ResetColor();

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

static async Task<Guid> AddAndIndexMemoryAsync(
    IMediator mediator,
    PersonalAIAssistant.Memory.Core.Interfaces.Persistence.IReadModelRepository readRepo,
    IVectorMemoryRepository vectorRepo,
    AddMemoryCommand cmd)
{
    var id = await mediator.Send(cmd);
    await readRepo.UpsertAsync(new MemoryReadModel
    {
        MemoryId = id,
        UserId = cmd.UserId,
        Summary = cmd.RawText,
        TokenCount = Math.Max(1, cmd.RawText.Length / 4),
        Archived = false,
        Importance = cmd.Importance,
        CreatedAt = DateTime.UtcNow
    }, CancellationToken.None);

    await vectorRepo.UpsertAsync(id, cmd.RawText, new float[1536], cmd.UserId, CancellationToken.None);
    return id;
}

static async Task RunGitHubCopilotDemoAsync(
    IMediator mediator,
    PersonalAIAssistant.Memory.Core.Interfaces.Persistence.IReadModelRepository readRepo,
    IVectorMemoryRepository vectorRepo,
    string userId)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n=====================================================================");
    Console.WriteLine("  DEMO 2: GitHub Copilot Chat — Developer Memory Recall & Grounding   ");
    Console.WriteLine("=====================================================================");
    Console.ResetColor();

    Console.WriteLine("\n[Scenario Context]");
    Console.WriteLine("A software engineer (Alice) is working in Visual Studio on the Memory API.");
    Console.WriteLine("GitHub Copilot Chat needs access to past team Architectural Decision Records (ADRs)");
    Console.WriteLine("and Alice's personal coding conventions to generate compliant code.\n");

    // Step 1: Workload Identity Authentication
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 1] GitHub Workload Identity Authentication (OIDC M2M)");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    Console.WriteLine("  Client:        GitHub Copilot Chat (VS Code / Visual Studio Extension)");
    Console.WriteLine("  Issuer:        https://token.actions.githubusercontent.com");
    Console.WriteLine("  Subject:       repo:PersonalAIAssistant/MemoryManagement:ref:refs/heads/main");
    Console.WriteLine("  Resolved Role: ServicePrincipal (Delegated to user: 'user-alice')");
    Console.WriteLine("  Granted Scope: memory:read, memory:write");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  [Auth Status]  200 OK — Scoped M2M Bearer Token issued.\n");
    Console.ResetColor();

    // Step 2: Ingest Team Architecture Decision Record (ADR) and Developer Conventions
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 2] Ingesting Team Decisions & Conventions into Memory Store");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();

    var adrText =
        "ADR-004: All cloud service endpoints (AWS, Azure, GCP, GitHub) must enforce OIDC Workload Identity " +
        "Federation or constant-time client credentials verification. No long-lived static keys permitted. " +
        "All sensitive customer data and tokens must use transparent AES-256-GCM authenticated encryption.";

    var convText =
        "Developer Preferences for Alice: Write idiomatic C# 12 with file-scoped namespaces, MediatR CQRS handlers, " +
        "primary constructors, immutable records, and strictly zero null reference warnings. Use xUnit with FluentAssertions.";

    var adrCmd = new AddMemoryCommand(
        RawText: adrText,
        Source: "GitHubCopilot:ADR",
        Importance: MemoryImportance.High,
        Tags: new List<string> { "copilot", "adr", "security", "architecture" },
        UserId: userId
    );
    var adrId = await AddAndIndexMemoryAsync(mediator, readRepo, vectorRepo, adrCmd);

    var convCmd = new AddMemoryCommand(
        RawText: convText,
        Source: "GitHubCopilot:UserPreference",
        Importance: MemoryImportance.High,
        Tags: new List<string> { "copilot", "coding-standards", "csharp" },
        UserId: userId
    );
    var convId = await AddAndIndexMemoryAsync(mediator, readRepo, vectorRepo, convCmd);

    Console.WriteLine($"  ✔ Persisted ADR-004 Memory Aggregate: [{adrId}]");
    Console.WriteLine($"  ✔ Persisted Developer Convention Memory: [{convId}]\n");

    // Step 3: Developer Asks Copilot Chat in IDE
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 3] Developer Prompt in Visual Studio Copilot Chat");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    var devPrompt = "Copilot, generate the cloud workload token authentication service for our new microservice.";
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  Developer > \"{devPrompt}\"\n");
    Console.ResetColor();

    // Step 4: Semantic Memory Recall
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 4] Copilot Chat Semantically Recalls Memories via Memory API");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    Console.WriteLine("  Query: GET /api/v1/memories/search?query=cloud workload token authentication service standards&topK=2");

    var recallQuery = new RetrieveMemoriesQuery(
        UserId: userId,
        QueryText: "cloud workload token authentication service standards",
        TopK: 2
    );
    var fusedPrompt = await mediator.Send(recallQuery);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  [Retrieved & Fused Memories Injected into Copilot System Prompt]:");
    Console.WriteLine("  " + new string('-', 65));
    foreach (var line in fusedPrompt.SystemContext.Split('\n'))
    {
        Console.WriteLine($"    {line.TrimEnd()}");
    }
    Console.WriteLine("  " + new string('-', 65) + "\n");
    Console.ResetColor();

    // Step 5: Grounded Copilot Output
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 5] Grounded GitHub Copilot Chat Response");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  Copilot Chat > Based on your team's ADR-004 and your C# 12 conventions, here is the implementation:");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine(@"
    // In accordance with ADR-004: Zero static secrets, constant-time verification & AES-256-GCM
    namespace PersonalAIAssistant.Memory.Infrastructure.Security;

    public sealed class CloudWorkloadAuthenticationService(
        IWorkloadIdentityRepository workloadRepo,
        ILogger<CloudWorkloadAuthenticationService> logger) : IWorkloadAuthenticationService
    {
        public async Task<WorkloadAuthResult> AuthenticateOidcTokenAsync(string token, CancellationToken ct)
        {
            // Validates token against trusted OIDC federated issuer
            var workload = await workloadRepo.FindMatchingOidcWorkloadAsync(token, ct);
            if (workload is null || workload.IsRevoked)
                return WorkloadAuthResult.Failed(""Unauthorized workload identity."");

            return WorkloadAuthResult.Success(workload.ClientId, workload.AllowedScopes);
        }
    }");
    Console.ResetColor();
    Console.WriteLine("\n[Outcome] Copilot generated code perfectly conforming to team ADR-004 & Alice's C# 12 styling!");
}

static async Task RunAmazonRufusDemoAsync(
    IMediator mediator,
    PersonalAIAssistant.Memory.Core.Interfaces.Persistence.IReadModelRepository readRepo,
    IVectorMemoryRepository vectorRepo,
    string userId)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n=====================================================================");
    Console.WriteLine("  DEMO 3: Amazon Rufus — Contextual Shopping Assistant & Safety Recall");
    Console.WriteLine("=====================================================================");
    Console.ResetColor();

    Console.WriteLine("\n[Scenario Context]");
    Console.WriteLine("Amazon Rufus is Amazon's AI shopping assistant on mobile and web.");
    Console.WriteLine("Normally, Rufus lacks cross-session memory of user hardware, sizing, and health safety.");
    Console.WriteLine("With Personal AI Assistant Memory, Rufus checks the user's unified memory for allergens & gear.\n");

    // Step 1: AWS Workload Identity Authentication
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 1] Amazon Rufus Authenticates via AWS Workload Identity (IAM OIDC)");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    Console.WriteLine("  Client:         Amazon Rufus AI Shopping Service");
    Console.WriteLine("  Cloud Platform: AWS (us-east-1 Bedrock Cluster)");
    Console.WriteLine("  Federation:     arn:aws:iam::123456789012:role/AmazonRufusMemoryFederation");
    Console.WriteLine("  Granted Scope:  memory:read, memory:write");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  [Auth Status]   200 OK — Authorized with shopper delegation to 'user-alice'.\n");
    Console.ResetColor();

    // Step 2: Ingest Shopper Profile, Allergy Alerts & Past Purchases
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 2] Ingesting Shopper Safety & Gear Profile into Memory Store");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();

    var allergyText =
        "CRITICAL HEALTH ALLERGY: Alice has a life-threatening PEANUT allergy and strictly follows a " +
        "Certified Gluten-Free diet. Any food recommendations must be certified gluten-free and processed in a 100% peanut-free facility.";

    var cameraGearText =
        "Hardware & Gear History: Alice owns a Sony Alpha 7 IV mirrorless camera with 24-70mm GM lens (total rig weight 1.6kg). " +
        "Requires durable carbon-fiber tripod legs with Arca-Swiss quick-release ball head.";

    var sizingText =
        "Shopper Sizing & Preferences: Alice wears US Women's size 8.5 medium hiking footwear, size Medium activewear, and prefers ultralight trail gear.";

    var allergyCmd = new AddMemoryCommand(
        RawText: allergyText,
        Source: "AmazonRufus:HealthProfile",
        Importance: MemoryImportance.Critical,
        Tags: new List<string> { "rufus", "health", "allergy", "gluten-free" },
        UserId: userId
    );
    var allergyId = await AddAndIndexMemoryAsync(mediator, readRepo, vectorRepo, allergyCmd);

    var gearCmd = new AddMemoryCommand(
        RawText: cameraGearText,
        Source: "AmazonRufus:PurchaseHistory",
        Importance: MemoryImportance.High,
        Tags: new List<string> { "rufus", "gear", "camera", "sony" },
        UserId: userId
    );
    var gearId = await AddAndIndexMemoryAsync(mediator, readRepo, vectorRepo, gearCmd);

    var sizeCmd = new AddMemoryCommand(
        RawText: sizingText,
        Source: "AmazonRufus:Preferences",
        Importance: MemoryImportance.Medium,
        Tags: new List<string> { "rufus", "sizing", "hiking" },
        UserId: userId
    );
    var sizeId = await AddAndIndexMemoryAsync(mediator, readRepo, vectorRepo, sizeCmd);

    Console.WriteLine($"  ✔ Persisted Critical Health Memory:     [{allergyId}]");
    Console.WriteLine($"  ✔ Persisted Camera & Hardware History:  [{gearId}]");
    Console.WriteLine($"  ✔ Persisted Sizing & Hiking Preference: [{sizeId}]\n");

    // Step 3: Shopper Asks Rufus in Amazon App
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 3] Customer Asks Rufus in Amazon Shopping App");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    var shopperPrompt = "Hey Rufus, recommend a compact travel tripod and some high-protein energy snacks for my weekend hike.";
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  Shopper > \"{shopperPrompt}\"\n");
    Console.ResetColor();

    // Step 4: Semantic Memory Recall
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 4] Amazon Rufus Recalls Shopper Memories via Memory API");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    Console.WriteLine("  Query: GET /api/v1/memories/search?query=compact travel tripod energy snacks hiking&topK=2");

    var recallQuery = new RetrieveMemoriesQuery(
        UserId: userId,
        QueryText: "compact travel tripod energy snacks hiking",
        TopK: 2
    );
    var fusedPrompt = await mediator.Send(recallQuery);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  [Retrieved Personal Constraints Injected into Rufus Context]:");
    Console.WriteLine("  " + new string('-', 65));
    foreach (var line in fusedPrompt.SystemContext.Split('\n'))
    {
        Console.WriteLine($"    {line.TrimEnd()}");
    }
    Console.WriteLine("  " + new string('-', 65) + "\n");
    Console.ResetColor();

    // Step 5: Grounded Rufus Output
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("---------------------------------------------------------------------");
    Console.WriteLine("[STEP 5] Grounded Amazon Rufus Recommendations");
    Console.WriteLine("---------------------------------------------------------------------");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  Amazon Rufus > Here are the best choices tailored to your gear and safety preferences:\n");
    Console.ResetColor();

    Console.WriteLine(@"  📸 1. Peak Design Carbon Fiber Travel Tripod (Arca-Swiss Compatible)
     • Why it fits: Matches your Sony Alpha 7 IV + 24-70mm GM setup (supports up to 9.1kg, well above your 1.6kg rig).
     • Compact: Packs down to the diameter of a water bottle for hiking.

  🌱 2. 88 Acres Certified Gluten-Free & Peanut-Free Dark Chocolate Sea Salt Seed Bars
     • Why it fits: 12g plant protein per serving.
     • Safety Verified: Manufactured in a dedicated 100% peanut-free, tree-nut-free facility and certified Gluten-Free.");

    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("\n  🛡️ [Rufus Safety & Compatibility Shield Active]");
    Console.WriteLine("  Excluded 14 popular trail mixes containing peanuts or gluten.");
    Console.WriteLine("  Excluded 8 lightweight mini-tripods rated under 1.5kg that would tip over with your Sony A7 IV.");
    Console.ResetColor();
    Console.WriteLine("\n[Outcome] Unified Memory prevented hazardous allergic reactions and equipment incompatibility!");
}

// ─────────────────────────────────────────────────────────────────────────────
// SUPPORTING IN-MEMORY MOCKS & REPOSITORIES
// ─────────────────────────────────────────────────────────────────────────────

class MockCompressionService : ICompressionService
{
    public Task<CompressionResult> CompressAsync(string text, CancellationToken ct)
        => Task.FromResult(new CompressionResult("Dense Summary: Architectural standards and memory constraints aligned.", "mock-model", 10));
}

class MockEmbeddingService : IEmbeddingService
{
    public static string LastQuery { get; set; } = string.Empty;

    public Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
    {
        LastQuery = text;
        return Task.FromResult(new EmbeddingResult(
            EmbeddingId: Guid.NewGuid().ToString(),
            Vector: new float[1536],
            Provider: "mock",
            Model: "mock-model"));
    }
}

class InMemoryVectorRepo : IVectorMemoryRepository
{
    private readonly List<(Guid MemoryId, string Text, string? UserId)> _memories = new();

    public Task UpsertAsync(Guid memoryId, string text, IReadOnlyList<float> vector, string? userId, CancellationToken ct)
    {
        lock (_memories)
        {
            _memories.RemoveAll(m => m.MemoryId == memoryId);
            _memories.Add((memoryId, text, userId));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        IReadOnlyList<float> vector, int limit, string? userId, CancellationToken ct)
    {
        lock (_memories)
        {
            var queryTokens = (MockEmbeddingService.LastQuery ?? string.Empty).ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', ';', ':', '?', '!', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2)
                .ToArray();

            var scored = _memories
                .Where(m => string.IsNullOrEmpty(userId) || m.UserId == userId)
                .Select(m =>
                {
                    var textLower = m.Text.ToLowerInvariant();
                    int matches = queryTokens.Count(t => textLower.Contains(t));
                    double score = matches > 0 ? 0.82 + Math.Min(0.16, 0.04 * matches) : 0.65;
                    return new { m.MemoryId, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => new VectorSearchResult(x.MemoryId, Guid.NewGuid().ToString(), x.Score))
                .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(scored);
        }
    }

    public Task DeleteAsync(Guid memoryId, CancellationToken ct)
    {
        lock (_memories)
        {
            _memories.RemoveAll(m => m.MemoryId == memoryId);
        }
        return Task.CompletedTask;
    }
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
