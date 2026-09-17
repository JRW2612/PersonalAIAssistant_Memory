namespace PersonalAIAssistant.Memory.Core.Models;

/// <summary>
/// A temporally grounded entity fact (Subject-Predicate-Object) with validity time window.
/// </summary>
public record TemporalFact(
    Guid Id,
    string Subject,
    string Predicate,
    string Object,
    DateTime ValidFromUtc,
    DateTime? ValidToUtc = null,
    bool IsCurrent = true,
    double Confidence = 1.0,
    string? Source = null);

/// <summary>
/// Request to assert a temporal fact.
/// </summary>
public record AssertFactRequest(
    string Subject,
    string Predicate,
    string Object,
    double Confidence = 1.0,
    string? Source = null);
