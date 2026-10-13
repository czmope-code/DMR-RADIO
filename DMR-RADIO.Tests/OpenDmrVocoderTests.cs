using DMR_RADIO.Audio;
using Xunit;

namespace DMR_RADIO.Tests;

public sealed class OpenDmrVocoderTests
{
    [Fact]
    public void CreateOrFallback_ProvidesVocoderAndExplainsUnavailableNativeLibrary()
    {
        using var vocoder = OpenDmrVocoder.CreateOrFallback(out var warning);

        if (warning is null)
            Assert.IsType<OpenDmrVocoder>(vocoder);
        else
            Assert.IsType<NullVocoder>(vocoder);
    }
}
