using System.Text.Json;
using DMR_RADIO.storage;
using Xunit;

namespace DMR_RADIO.Tests;

public sealed class AudioOutputSettingsTests
{
    [Fact]
    public void ExistingSettingsWithoutAudioDevice_DefaultToWindowsOutput()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"DmrId":"2301234","Server":"localhost","Port":"62031","TalkGroup":"230","Volume":70}""");

        Assert.NotNull(settings);
        Assert.Equal(-1, settings.AudioOutputDeviceNumber);
    }

    [Fact]
    public void AudioOutputDeviceNumber_IsPersistedInSettingsJson()
    {
        var json = JsonSerializer.Serialize(new AppSettings { AudioOutputDeviceNumber = 2 });
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.AudioOutputDeviceNumber);
    }
}
