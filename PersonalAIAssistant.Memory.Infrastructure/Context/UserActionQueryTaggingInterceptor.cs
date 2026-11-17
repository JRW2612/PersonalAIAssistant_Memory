using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PersonalAIAssistant.Memory.Core.Diagnostics;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Context;

/// <summary>
/// EF Core DbCommandInterceptor that ensures all executed SQL commands contain a user action tag header
/// in the SQL comment text and tracks database execution timing and errors for 504 triage.
/// </summary>
public class UserActionQueryTaggingInterceptor : DbCommandInterceptor
{
    private readonly IDbQueryTagger _tagger;
    private readonly IUserContext? _userContext;
    private readonly IRequestTimingContext? _timingContext;

    public UserActionQueryTaggingInterceptor(
        IDbQueryTagger tagger,
        IUserContext? userContext = null,
        IRequestTimingContext? timingContext = null)
    {
        _tagger = tagger;
        _userContext = userContext;
        _timingContext = timingContext;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        EnsureTagged(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        EnsureTagged(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        RecordExecution(command, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordExecution(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        EnsureTagged(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        EnsureTagged(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        RecordExecution(command, eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        RecordExecution(command, eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        EnsureTagged(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnsureTagged(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        RecordExecution(command, eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RecordExecution(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(
        DbCommand command,
        CommandErrorEventData eventData)
    {
        RecordFailure(command, eventData.Exception);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordFailure(command, eventData.Exception);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    public virtual string? EnsureTagged(DbCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CommandText))
            return null;

        // If the query was already tagged via .TagWith(), do not duplicate tags
        if (command.CommandText.Contains("UserAction:") || command.CommandText.Contains("-- Action:"))
            return null;

        var action = !string.IsNullOrWhiteSpace(_userContext?.CurrentAction)
            ? _userContext.CurrentAction
            : "DbCommandExecution";

        var tag = _tagger.CreateTag(action);
        command.CommandText = $"-- {tag}{Environment.NewLine}{command.CommandText}";
        return tag;
    }

    private void RecordExecution(DbCommand command, TimeSpan duration)
    {
        if (_timingContext == null) return;
        var tag = ExtractTag(command.CommandText);
        _timingContext.RecordDatabaseExecution(duration, tag, command.CommandText);
    }

    private void RecordFailure(DbCommand command, Exception ex)
    {
        if (_timingContext == null) return;
        var tag = ExtractTag(command.CommandText);
        _timingContext.RecordDatabaseFailure(ex, tag, command.CommandText);
    }

    private static string? ExtractTag(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return null;

        // Check for "-- UserAction: ..." or "-- Action: ..." or "/* UserAction: ... */"
        var lines = commandText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("-- UserAction:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("-- Action:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.TrimStart('-', ' ').Trim();
            }

            if (trimmed.Contains("UserAction:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("UserAction:", StringComparison.OrdinalIgnoreCase);
                var endIdx = trimmed.IndexOf("*/", idx, StringComparison.OrdinalIgnoreCase);
                return endIdx > idx
                    ? trimmed.Substring(idx, endIdx - idx).Trim()
                    : trimmed.Substring(idx).Trim();
            }
        }

        return null;
    }
}
