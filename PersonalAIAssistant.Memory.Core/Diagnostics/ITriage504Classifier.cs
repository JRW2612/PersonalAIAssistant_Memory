namespace PersonalAIAssistant.Memory.Core.Diagnostics;

/// <summary>
/// Contract for binary isolation triage of 504 Gateway Timeout incidents.
/// Deterministically isolates failures between Client-to-Gateway, Gateway-to-Service, and Service-to-Database.
/// </summary>
public interface ITriage504Classifier
{
    TimeoutTriageResult Classify(
        Exception? exception,
        HopTimingMetrics metrics,
        string? activeQueryTag = null,
        string? offendingCommand = null,
        bool clientAbortedBeforeDispatch = false
    );
}
