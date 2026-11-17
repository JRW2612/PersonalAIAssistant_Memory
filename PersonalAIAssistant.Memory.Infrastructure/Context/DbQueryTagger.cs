using System.Diagnostics;
using System.IO;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Context;

/// <summary>
/// Default implementation of IDbQueryTagger. Formats contextual query tags
/// containing user action, user ID, tenant ID, and trace/correlation ID for EF Core .TagWith().
/// Sanitizes values to ensure safe database comment output across database engines.
/// </summary>
public sealed class DbQueryTagger : IDbQueryTagger
{
    private readonly IUserContext? _userContext;

    public DbQueryTagger(IUserContext? userContext = null)
    {
        _userContext = userContext;
    }

    public string CreateTag(
        string actionName,
        string? callerMember = null,
        string? callerFile = null,
        int callerLine = 0)
    {
        var action = Sanitize(string.IsNullOrWhiteSpace(actionName)
            ? (!string.IsNullOrWhiteSpace(_userContext?.CurrentAction) ? _userContext.CurrentAction : callerMember ?? "UnknownAction")
            : actionName);

        var userId = Sanitize(!string.IsNullOrWhiteSpace(_userContext?.UserId) ? _userContext.UserId : "system");
        var tenantId = Sanitize(!string.IsNullOrWhiteSpace(_userContext?.TenantId) ? _userContext.TenantId : "default");

        var correlationId = _userContext?.CorrelationId
            ?? Activity.Current?.TraceId.ToString()
            ?? "none";
        correlationId = Sanitize(correlationId);

        var tag = $"UserAction: {action} | User: {userId} | Tenant: {tenantId} | TraceId: {correlationId}";

        if (!string.IsNullOrWhiteSpace(callerMember))
        {
            var fileName = !string.IsNullOrWhiteSpace(callerFile) ? Path.GetFileName(callerFile) : null;
            if (fileName != null && callerLine > 0)
            {
                tag += $" | CallSite: {fileName}:{callerLine}#{callerMember}()";
            }
            else
            {
                tag += $" | CallSite: {callerMember}()";
            }
        }

        return tag;
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        return input
            .Replace("/*", "")
            .Replace("*/", "")
            .Replace("--", "-")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }
}
