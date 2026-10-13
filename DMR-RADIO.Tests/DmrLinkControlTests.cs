using DMR_RADIO.Protocol;
using Xunit;

namespace DMR_RADIO.Tests;

public sealed class DmrLinkControlTests
{
    [Fact]
    public void LinkControlData_UsesGroupCallAndBigEndianIds()
    {
        var data = DmrLinkControl.BuildLinkControlData(0x112233, 0x0ABCDE, false);

        Assert.Equal(new byte[] { 0, 0, 0, 0x0A, 0xBC, 0xDE, 0x11, 0x22, 0x33 }, data[..9]);
        Assert.Equal(12, data.Length);
    }

    [Fact]
    public void HeaderAndTerminator_ProduceDistinct33ByteDataBursts()
    {
        var header = DmrLinkControl.BuildVoiceLcHeaderBurst(2301234, 230);
        var terminator = DmrLinkControl.BuildTerminatorBurst(2301234, 230);

        Assert.Equal(33, header.Length);
        Assert.Equal(33, terminator.Length);
        Assert.NotEqual(header, terminator);
    }

    [Fact]
    public void LinkControlData_RejectsIdentifiersOutside24Bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DmrLinkControl.BuildLinkControlData(0x1000000, 230, false));
    }
}
