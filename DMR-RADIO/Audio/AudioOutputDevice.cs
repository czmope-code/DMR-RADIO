using NAudio.Wave;

namespace DMR_RADIO.Audio;

public sealed record AudioOutputDevice(int DeviceNumber, string Name)
{
    public override string ToString() => Name;

    public static IReadOnlyList<AudioOutputDevice> Enumerate()
    {
        var devices = new List<AudioOutputDevice>
        {
            new(-1, "Výchozí zařízení Windows")
        };

        try
        {
            for (var deviceNumber = 0; deviceNumber < WaveOut.DeviceCount; deviceNumber++)
            {
                var name = WaveOut.GetCapabilities(deviceNumber).ProductName;
                devices.Add(new AudioOutputDevice(deviceNumber, name));
            }
        }
        catch (Exception)
        {
        }

        return devices;
    }
}
