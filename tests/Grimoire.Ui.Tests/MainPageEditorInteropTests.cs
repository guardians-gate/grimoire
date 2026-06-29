using System.Text.Json;

namespace Grimoire.Ui.Tests;

/// <summary>
/// Represents regression tests for JavaScript interop payload decoding used by the source editor bridge.
/// </summary>
public sealed class MainPageEditorInteropTests
{
    /// <summary>
    /// Verifies that envelope JSON payloads are decoded into plain editor text and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void DecodeJavaScriptStringResultDecodesInteropEnvelopePayload()
    {
        string payload = JsonSerializer.Serialize(new
        {
            type = "source-editor-text",
            text = "line one\nline two",
        });

        string decoded = MainPage.DecodeJavaScriptStringResult(payload);

        Assert.Equal("line one\nline two", decoded);
    }

    /// <summary>
    /// Verifies that double-encoded envelope payloads are decoded into plain editor text and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void DecodeJavaScriptStringResultDecodesDoubleEncodedInteropEnvelopePayload()
    {
        string payload = JsonSerializer.Serialize(new
        {
            type = "source-editor-text",
            text = "first line\nsecond line",
        });
        string encoded = JsonSerializer.Serialize(payload);

        string decoded = MainPage.DecodeJavaScriptStringResult(encoded);

        Assert.Equal("first line\nsecond line", decoded);
    }

    /// <summary>
    /// Verifies that legacy JSON-string payloads remain supported by the decoder and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void DecodeJavaScriptStringResultRetainsLegacyJsonStringDecoding()
    {
        string encoded = JsonSerializer.Serialize("alpha\nbeta");

        string decoded = MainPage.DecodeJavaScriptStringResult(encoded);

        Assert.Equal("alpha\nbeta", decoded);
    }
}
