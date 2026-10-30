namespace PersonalAIAssistant.Memory.Core.Models
{
    /// <summary>
    /// Supported modalities for visual vector memory inputs.
    /// </summary>
    public enum VisualModality
    {
        CameraSnapshot,
        WhiteboardDrawing,
        DocumentScan,
        ScreenCapture,
        Diagram,
        GeneralImage
    }

    /// <summary>
    /// Represents visual data (e.g. camera photos, whiteboard drawings) to be embedded and indexed into memory.
    /// </summary>
    public sealed class VisualContent
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public string MimeType { get; init; } = "image/jpeg";
        public string? Caption { get; init; }
        public VisualModality Modality { get; init; } = VisualModality.GeneralImage;
        public string? Filename { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }

        public VisualContent() { }

        public VisualContent(byte[] data, string mimeType = "image/jpeg", string? caption = null, VisualModality modality = VisualModality.GeneralImage)
        {
            Data = data ?? Array.Empty<byte>();
            MimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
            Caption = caption;
            Modality = modality;
        }

        public static VisualContent FromBase64(string base64Data, string mimeType = "image/jpeg", string? caption = null, VisualModality modality = VisualModality.GeneralImage)
        {
            if (string.IsNullOrWhiteSpace(base64Data))
            {
                return new VisualContent(Array.Empty<byte>(), mimeType, caption, modality);
            }

            // Strip optional data URI scheme prefix (e.g., data:image/png;base64,...)
            var commaIdx = base64Data.IndexOf(',');
            var rawBase64 = (commaIdx >= 0 && base64Data.Contains(";base64"))
                ? base64Data[(commaIdx + 1)..]
                : base64Data;

            var bytes = Convert.FromBase64String(rawBase64);
            return new VisualContent(bytes, mimeType, caption, modality);
        }
    }

    /// <summary>
    /// Request to generate a unified multimodal embedding combining text and visual inputs.
    /// </summary>
    public sealed class MultimodalEmbeddingRequest
    {
        public string? Text { get; init; }
        public VisualContent? Visual { get; init; }
        public double TextWeight { get; init; } = 0.5;
        public double VisualWeight { get; init; } = 0.5;

        public MultimodalEmbeddingRequest() { }

        public MultimodalEmbeddingRequest(string? text, VisualContent? visual, double textWeight = 0.5, double visualWeight = 0.5)
        {
            Text = text;
            Visual = visual;
            TextWeight = textWeight;
            VisualWeight = visualWeight;
        }
    }
}
