using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Infrastructure.Context;

namespace PersonalAIAssistant.Memory.Infrastructure.Extensions;

/// <summary>
/// Extension methods for tagging EF Core IQueryable queries with user action and identity metadata
/// using EF Core's native .TagWith() method, so database logs (Postgres, slow query logs, APM, etc.)
/// show which user action caused the query.
/// </summary>
public static class EfQueryTaggingExtensions
{
    /// <summary>
    /// Tags the EF Core query with user action, user identity, tenant, and trace metadata using an IDbQueryTagger.
    /// </summary>
    public static IQueryable<T> TagWithUserAction<T>(
        this IQueryable<T> query,
        IDbQueryTagger? tagger,
        string action,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var tag = tagger != null
            ? tagger.CreateTag(action, callerMember, callerFile, callerLine)
            : FormatTag(null, action, callerMember, callerFile, callerLine);

        return query.TagWith(tag);
    }

    /// <summary>
    /// Tags the EF Core query with user action, user identity, tenant, and trace metadata using an IUserContext.
    /// </summary>
    public static IQueryable<T> TagWithUserAction<T>(
        this IQueryable<T> query,
        IUserContext? userContext,
        string action,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var tag = FormatTag(userContext, action, callerMember, callerFile, callerLine);
        return query.TagWith(tag);
    }

    /// <summary>
    /// Formats a standardized query tag string for database logging.
    /// </summary>
    public static string FormatTag(
        IUserContext? userContext,
        string action,
        string? callerMember = null,
        string? callerFile = null,
        int callerLine = 0)
    {
        var tagger = new DbQueryTagger(userContext);
        return tagger.CreateTag(action, callerMember, callerFile, callerLine);
    }
}
