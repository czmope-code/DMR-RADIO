using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DmrRadio.Audio;
using Moq;

public class JitterBufferTests
{
    [Fact]
    public async Task EnqueueFrame_ResultsInDecodeAndPlayback()
    {
        var vocoderMock = new Mock<IVocoder>();
        vocoderMock.Setup(v => v.Decode(It.IsAny<ReadOnlySpan<byte>>())).Returns(new short[160]);

        var playerMock = new Mock<AudioPlayer>();
        // for testing, we could subclass AudioPlayer to record AddPcm calls or mock a simple interface

        // Use a lightweight wrapper for testing instead of real AudioPlayer
        var testPlayer = new TestAudioPlayer();

        using var jb = new JitterBuffer(vocoderMock.Object, testPlayer);
        jb.Start();
        jb.AddFrame(new byte[9]);
        await Task.Delay(100); // allow background task to run
        jb.Stop();

        Assert.True(testPlayer.AddedCount > 0);
    }

    class TestAudioPlayer : AudioPlayer
    {
        public int AddedCount { get; private set; } = 0;
        public override void AddPcm(short[] pcm)
        {
            AddedCount++;
            base.AddPcm(pcm);
        }
    }
}