using DMR_RADIO.Audio;
using Xunit;

namespace DMR_RADIO.Tests;

public sealed class DmrVoiceBurstCodecTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateAndExtract_RoundTripThreeAmbeFrames(bool voiceSync)
    {
        var frames = Enumerable.Range(0, 3)
            .Select(frame => Enumerable.Range(0, 9).Select(i => (byte)(frame * 31 + i * 7)).ToArray())
            .ToArray();

        var burst = DmrVoiceBurstCodec.CreateVoiceBurst(frames, voiceSync);
        var extracted = DmrVoiceBurstCodec.ExtractAmbeFrames(burst);

        Assert.Equal(frames, extracted);
    }

    [Fact]
    public void ExtractAmbeFrames_RejectsInvalidBurstLength()
    {
        Assert.Throws<ArgumentException>(() => DmrVoiceBurstCodec.ExtractAmbeFrames(new byte[32]));
    }

    [Fact]
    public void ExtractAmbeFrames_AccountsForTheSplitSecondFrame()
    {
        var burst = new byte[33];
        SetBit(burst, 40);
        SetBit(burst, 160);
        SetBit(burst, 232);

        var frames = DmrVoiceBurstCodec.ExtractAmbeFrames(burst);

        Assert.True(GetBit(frames[0], 40));
        Assert.True(GetBit(frames[1], 40));
        Assert.True(GetBit(frames[2], 40));
    }

    private static void SetBit(Span<byte> data, int bit)
        => data[bit / 8] |= (byte)(1 << (7 - bit % 8));

    private static bool GetBit(ReadOnlySpan<byte> data, int bit)
        => (data[bit / 8] & (1 << (7 - bit % 8))) != 0;
}
