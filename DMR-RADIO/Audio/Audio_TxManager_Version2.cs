using System.Diagnostics;
using DMR_RADIO.Protocol;

namespace DMR_RADIO.Audio;

public sealed class TxManager : IDisposable
{
    private readonly HomebrewClient _client;
    private readonly IVocoder _vocoder;
    private readonly uint _source;
    private readonly uint _destination;
    private readonly uint _repeaterId;
    private readonly bool _slot2;
    private readonly uint _streamId;
    private readonly Action<string> _log;
    private byte _sequence;
    private int _burstIndex;

    public TxManager(HomebrewClient client, IVocoder vocoder, uint source, uint destination,
        uint repeaterId, bool slot2, uint streamId, Action<string> log)
    {
        _client = client;
        _vocoder = vocoder;
        _source = source;
        _destination = destination;
        _repeaterId = repeaterId;
        _slot2 = slot2;
        _streamId = streamId;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(DmrFrameType.DataSync, 1,
                DmrLinkControl.BuildVoiceLcHeaderBurst(_source, _destination), cancellationToken);
            _log($"TX: LC hlavička → TG {_destination} (TS{(_slot2 ? 2 : 1)}).");

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(60));
            var started = Stopwatch.StartNew();
            var silence = new short[160];
            var ambe = new[] { new byte[9], new byte[9], new byte[9] };

            while (started.Elapsed < TimeSpan.FromSeconds(180) &&
                   await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                for (var frame = 0; frame < ambe.Length; frame++)
                    ambe[frame] = _vocoder.Encode(silence);

                var burstSequence = (byte)(_burstIndex++ % 6);
                var voiceSync = burstSequence == 0;
                await SendAsync(
                    voiceSync ? DmrFrameType.VoiceSync : DmrFrameType.Voice,
                    burstSequence,
                    DmrVoiceBurstCodec.CreateVoiceBurst(ambe, voiceSync),
                    cancellationToken).ConfigureAwait(false);
            }

            if (started.Elapsed >= TimeSpan.FromSeconds(180))
                _log("TX: dosažen bezpečnostní limit 180 s.");
        }
        finally
        {
            try
            {
                await SendAsync(DmrFrameType.DataSync, 2,
                    DmrLinkControl.BuildTerminatorBurst(_source, _destination), CancellationToken.None)
                    .ConfigureAwait(false);
                _log($"TX: terminátor → TG {_destination}.");
            }
            catch (Exception ex)
            {
                _log($"TX: terminátor se nepodařilo odeslat: {ex.Message}");
            }
        }
    }

    private Task SendAsync(DmrFrameType frameType, byte dataType, byte[] burst, CancellationToken token)
        => _client.SendDmrdAsync(new DmrdPacket(
            _sequence++, _source, _destination, _repeaterId, _slot2, false,
            frameType, dataType, _streamId, burst, 0, 0), token);

    public void Dispose() => _vocoder.Dispose();
}
