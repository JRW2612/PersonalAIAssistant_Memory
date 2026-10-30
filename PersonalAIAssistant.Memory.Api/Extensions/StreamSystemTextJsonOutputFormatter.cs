using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace PersonalAIAssistant.Memory.Api.Extensions;

/// <summary>
/// Output formatter that serializes directly to the Response Body Stream.
/// Guarantees seamless compatibility across both .NET 8 Kestrel and .NET 10 Preview TestHost runners
/// by bypassing PipeWriter.UnflushedBytes incompatibilities in in-memory test harnesses.
/// </summary>
public class StreamSystemTextJsonOutputFormatter : TextOutputFormatter
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public StreamSystemTextJsonOutputFormatter(JsonSerializerOptions jsonSerializerOptions)
    {
        _jsonSerializerOptions = jsonSerializerOptions;
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/*+json"));
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        var response = context.HttpContext.Response;
        var value = context.Object;
        var type = context.ObjectType ?? value?.GetType() ?? typeof(object);

        await JsonSerializer.SerializeAsync(response.Body, value, type, _jsonSerializerOptions, context.HttpContext.RequestAborted);
    }
}
