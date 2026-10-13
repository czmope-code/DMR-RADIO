using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmrRadio.Audio
{
    /// <summary>
    /// Simple TX manager that, while PTT is down, takes PCM->AMBE frames from the IVocoder,
    /// packs 3 frames into a voice burst and calls INetworkVoiceSender.SendVoiceBurst.
    /// With NullVocoder this will send silent bursts (allows "joining" TG).
    /// Replace IVocoder to get real TX.
    /// </summary>
    public class TxManager : IDisposable
    {
        private readonly IVocoder _vocoder;
        private readonly INetworkVoiceSender _sender;
        private CancellationTokenSource _cts;
        private Task _txTask;

        public TxManager(IVocoder vocoder, INetworkVoiceSender sender)
        {
            _vocoder = vocoder ?? throw new ArgumentNullException(nameof(vocoder));
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        /// <summary>
        /// Start continuous TX (PTT pressed). Call StopTx() to end.
        /// </summary>
        public void StartTx(Func<short[]> capturePcmFrame /* should return 160 samples or null when no data */)
        {
            if (_txTask != null && !_txTask.IsCompleted) return;
            _cts = new CancellationTokenSource();
            _txTask = Task.Run(() => TxLoop(capturePcmFrame, _cts.Token));
        }

        public void StopTx()
        {
            _cts?.Cancel();
            try { _txTask?.Wait(500); } catch { }
            _cts?.Dispose();
            _txTask = null;
        }

        private async Task TxLoop(Func<short[]> capturePcmFrame, CancellationToken token)
        {
            // accumulate 3 AMBE frames into one DMR burst
            var frameList = new List<byte>(9 * 3);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // capture PCM (160 samples) from a provided callback (can be microphone capture or silence)
                    var pcm = capturePcmFrame?.Invoke() ?? new short[160];
                    var ambe = _vocoder.Encode(pcm); // 9 bytes
                    frameList.AddRange(ambe);
                    if (frameList.Count >= 9 * 3)
                    {
                        // Create DMR burst payload from these 27 bytes.
                        var burst = frameList.ToArray();
                        // Caller must prepend the DMR voice header/sequence/streaming metadata as required.
                        _sender.SendVoiceBurst(burst);
                        frameList.Clear();
                    }
                    // real-time pacing: 20ms per frame -> we want to produce frames continuously
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Dispose()
        {
            StopTx();
            _vocoder.Dispose();
        }
    }
}