using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Api.DTOs;

/// <summary>
/// Request payload for ingesting visual memories (camera snapshots, whiteboard drawings, diagrams).
/// </summary>
public record CreateVisualMemoryDto(
    string? Description,
    string ImageBase64,
    string MimeType = "image/jpeg",
    VisualModality Modality = VisualModality.CameraSnapshot,
    string? Source = "VisualCapture",
    MemoryImportance Importance = MemoryImportance.Medium,
    List<string>? Tags = null,
    string? CorrelationId = null
);

/// <summary>
/// Request payload for searching memories via visual inputs (camera snapshots, sketches, whiteboard photos).
/// </summary>
public record SearchVisualMemoryDto(
    string? QueryText,
    string ImageBase64,
    string MimeType = "image/jpeg",
    VisualModality Modality = VisualModality.GeneralImage,
    int TopK = 5
);
