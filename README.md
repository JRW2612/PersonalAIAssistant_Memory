# PersonalAIAssistant_Memory

[![CI](https://github.com/JRW2612/PersonalAIAssistant_Memory/actions/workflows/ci.yml/badge.svg)](https://github.com/JRW2612/PersonalAIAssistant_Memory/actions/workflows/ci.yml)
[![Codecov](https://codecov.io/gh/JRW2612/PersonalAIAssistant_Memory/branch/main/graph/badge.svg)](https://codecov.io/gh/JRW2612/PersonalAIAssistant_Memory)
[![CodeQL](https://github.com/JRW2612/PersonalAIAssistant_Memory/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/JRW2612/PersonalAIAssistant_Memory/actions/workflows/codeql-analysis.yml)
[![Dependabot Status](https://github.com/JRW2612/PersonalAIAssistant_Memory/security/dependabot)](https://github.com/JRW2612/PersonalAIAssistant_Memory/security/dependabot)

Short description: Memory management services used by the Personal AI Assistant project. CI builds and tests on .NET 8 and .NET 10.

## CI

Runs on pushes to main and Feature_MemoryManagement_Jayant and on pull requests to main. Produces test results, coverage reports and packs artifacts.

## Contributing

Run `dotnet tool restore` to install required tools (dotnet-format, reportgenerator). Use `dotnet format` to autoformat code before PRs.

## Agent and meeting integrations

The API exposes an authenticated, stateless MCP endpoint at `/mcp`. Configure Codex, Google Antigravity, or another Streamable HTTP MCP client to connect to that URL and send the user's API JWT as a Bearer token. It exposes:

- `memory_search(query, topK?)` to retrieve relevant memories for the signed-in user.
- `memory_save(content, source?)` to save a decision or fact for that user.

Example Antigravity MCP server configuration:

```json
{
  "mcpServers": {
    "personal-memory": {
      "serverUrl": "https://YOUR_MEMORY_API/mcp",
      "headers": { "Authorization": "Bearer YOUR_USER_JWT" }
    }
  }
}
```

Meeting providers are connected per user through `GET /api/v1/connections/{teams|zoom|meet}/authorize`. Configure OAuth client credentials through environment variables or a secret store:

- `ProviderConnections__Microsoft__ClientId`, `ProviderConnections__Microsoft__ClientSecret`, and optionally `ProviderConnections__Microsoft__Tenant`.
- `ProviderConnections__Zoom__ClientId` and `ProviderConnections__Zoom__ClientSecret`.
- `ProviderConnections__Google__ClientId` and `ProviderConnections__Google__ClientSecret` for Meet (the same Google OAuth app can also support the existing Gemini connection).

The provider OAuth callback stores encrypted tokens and the scopes actually granted by the provider. The application rejects direct/BYOK token connection for meeting providers so transcript permission cannot be self-declared. After a meeting, call `POST /api/v1/meetings/{teams|zoom|meet}/{meetingId}/import`; the API uses the user's stored provider token to fetch the transcript and stores it as a searchable memory. Teams requires `organizerId`; `transcriptId` can be supplied when the provider has multiple transcript artifacts. A transcript-push endpoint is also available at `POST /api/v1/meetings/{platform}/transcripts`. Repeated delivery of the same transcript is idempotent.

Each provider still needs its app registration, transcript permission, and tenant consent. A meeting end event or scheduled job should call the import endpoint; this service does not join or record meetings and does not create provider webhook subscriptions.

Claude is available through the `anthropic` AI provider adapter. Configure `AI__Anthropic__ApiKey` and optionally `AI__Anthropic__Model`. When using the private AI gateway, configure `Upstreams__Anthropic__ApiKey` in the gateway's secret store; the gateway intentionally does not forward user-supplied provider credentials to external upstreams. For per-user Anthropic keys, disable the gateway for the memory API and call Anthropic directly. The provider participates in the same governance allowlist as Gemini and OpenAI.
