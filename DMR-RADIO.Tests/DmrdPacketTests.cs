using DMR_RADIO.Protocol;
using Xunit;

namespace DMR_RADIO.Tests;

public sealed class DmrdPacketTests
{
    [Fact]
    public void ToBytesAndTryParse_RoundTripAllFields()
    {
        var packet = new DmrdPacket(17, 2301234, 230, 0x12345678, true, false,
            DmrFrameType.VoiceSync, 7, 0xAABBCCDD, Enumerable.Range(0, 33).Select(i => (byte)i).ToArray(), 12, 34);

        var bytes = packet.ToBytes();

        Assert.Equal(55, bytes.Length);
        Assert.True(DmrdPacket.TryParse(bytes, out var parsed));
        Assert.Equal(packet.Sequence, parsed.Sequence);
        Assert.Equal(packet.Source, parsed.Source);
        Assert.Equal(packet.Destination, parsed.Destination);
        Assert.Equal(packet.RepeaterId, parsed.RepeaterId);
        Assert.Equal(packet.Slot2, parsed.Slot2);
        Assert.Equal(packet.IsPrivate, parsed.IsPrivate);
        Assert.Equal(packet.FrameType, parsed.FrameType);
        Assert.Equal(packet.DataType, parsed.DataType);
        Assert.Equal(packet.StreamId, parsed.StreamId);
        Assert.Equal(packet.Burst, parsed.Burst);
    }

    [Fact]
    public void TryParse_RejectsPacketsShorterThan55Bytes()
    {
        Assert.False(DmrdPacket.TryParse(new byte[54], out _));
    }

    [Fact]
    public void ToBytes_RejectsIdentifiersOutside24Bits()
    {
        var packet = new DmrdPacket(0, 0x1000000, 1, 0, false, false,
            DmrFrameType.Voice, 0, 1, new byte[33], 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => packet.ToBytes());
    }
}
