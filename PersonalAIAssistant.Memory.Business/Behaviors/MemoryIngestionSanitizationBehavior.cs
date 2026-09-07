using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Behaviors;

/// <summary>Replaces mutable memory payloads with their scrubbed, categorized representation before persistence.</summary>
public sealed class MemoryIngestionSanitizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IMemoryIngestionSanitizer _sanitizer;
    private readonly MemoryIngestionOptions _options;

    public MemoryIngestionSanitizationBehavior(IMemoryIngestionSanitizer sanitizer, Microsoft.Extensions.Options.IOptions<MemoryIngestionOptions> options)
    {
        _sanitizer = sanitizer;
        _options = options.Value;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return next(cancellationToken);

        request = request switch
        {
            AddMemoryCommand command => (TRequest)(object)Sanitize(command),
            UpdateMemoryCommand command => (TRequest)(object)Sanitize(command),
            ConsolidateMemoriesCommand command => (TRequest)(object)Sanitize(command),
            _ => request
        };
        return next(cancellationToken);
    }

    private AddMemoryCommand Sanitize(AddMemoryCommand command)
    {
        var value = _sanitizer.Sanitize(command.RawText, command.Tags);
        return command with { RawText = value.Text, Tags = value.Tags };
    }

    private UpdateMemoryCommand Sanitize(UpdateMemoryCommand command)
    {
        if (command.UpdatedFields is null || !command.UpdatedFields.TryGetValue("RawText", out var rawText)) return command;
        var value = _sanitizer.Sanitize(rawText, null);
        var fields = new Dictionary<string, string>(command.UpdatedFields) { ["RawText"] = value.Text };
        return command with { UpdatedFields = fields };
    }

    private ConsolidateMemoriesCommand Sanitize(ConsolidateMemoriesCommand command)
    {
        var value = _sanitizer.Sanitize(command.ConsolidatedText, null);
        return command with { ConsolidatedText = value.Text };
    }
}
