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
    public void HeaderBurst_ContainsBptcLinkControlDataAndGolaySlotType()
    {
        const uint source = 0x112233;
        const uint destination = 0x0ABCDE;
        var burst = DmrLinkControl.BuildVoiceLcHeaderBurst(source, destination, 1);

        Assert.Equal(DmrLinkControl.BuildLinkControlData(source, destination, false)[..9],
            DecodeBptcInformation(burst)[..9]);

        uint slotType = 0;
        for (var bit = 0; bit < 10; bit++)
            slotType = (slotType << 1) | (GetBit(burst, 98 + bit) ? 1u : 0u);
        for (var bit = 0; bit < 10; bit++)
            slotType = (slotType << 1) | (GetBit(burst, 156 + bit) ? 1u : 0u);
        Assert.Equal(0x11B8Cu, slotType);
    }

    [Fact]
    public void LinkControlData_RejectsIdentifiersOutside24Bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DmrLinkControl.BuildLinkControlData(0x1000000, 230, false));
    }

    private static byte[] DecodeBptcInformation(ReadOnlySpan<byte> burst)
    {
        var interleaved = new bool[196];
        for (var bit = 0; bit < 98; bit++)
            interleaved[bit] = GetBit(burst, bit);
        for (var bit = 98; bit < 196; bit++)
            interleaved[bit] = GetBit(burst, bit + 68);

        var matrix = new bool[196];
        for (var bit = 0; bit < matrix.Length; bit++)
            matrix[bit] = interleaved[(bit * 181) % 196];

        var payload = new byte[12];
        var payloadBit = 0;
        for (var column = 4; column <= 11; column++)
            SetBit(payload, payloadBit++, matrix[column]);
        for (var row = 1; row <= 8; row++)
        {
            for (var column = 1; column <= 11; column++)
                SetBit(payload, payloadBit++, matrix[row * 15 + column]);
        }
        return payload;
    }

    private static bool GetBit(ReadOnlySpan<byte> data, int bit)
        => (data[bit / 8] & (1 << (7 - bit % 8))) != 0;

    private static void SetBit(Span<byte> data, int bit, bool value)
    {
        if (value)
            data[bit / 8] |= (byte)(1 << (7 - bit % 8));
    }
}
