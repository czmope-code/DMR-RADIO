using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DmrRadio.Audio
{
    /// <summary>
    /// Very small jitter buffer: accept incoming raw 9-byte AMBE frames and decode+play via IVocoder+AudioPlayer.
    /// Consumer runs on a background Task. Adjustable target latency in ms (approx).
    /// </summary>
    public class JitterBuffer : IDisposable
    {
        private readonly BlockingCollection<byte[]> _queue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
        private readonly IVocoder _vocoder;
        private readonly AudioPlayer _player;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _consumer;
        public int TargetLatencyMs { get; set; } = 60;

        public JitterBuffer(IVocoder vocoder, AudioPlayer player)
        {
            _vocoder = vocoder ?? throw new ArgumentNullException(nameof(vocoder));
            _player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public void Start()
        {
            _player.Start();
            _consumer = Task.Run(ConsumeLoop, _cts.Token);
        }

        public void Stop()
        {
            _cts.Cancel();
            _queue.CompleteAdding();
            try { _consumer?.Wait(500); } catch { }
            _player.Stop();
        }

        /// <summary>
        /// Incoming AMBE frame (should be 9 bytes). Caller filters by TG/slot before enqueue.
        /// </summary>
        public void AddFrame(byte[] ambe9)
        {
            if (ambe9 == null) return;
            if (ambe9.Length != 9) return;
            // Best-effort enqueue, drop if full
            _queue.TryAdd(ambe9);
        }

        private async Task ConsumeLoop()
        {
            var token = _cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_queue.TryTake(out var ambe, 200))
                    {
                        try
                        {
                            var pcm = _vocoder.Decode(ambe);
                            _player.AddPcm(pcm);
                        }
                        catch
                        {
                            // on decode errors, add silence
                            _player.AddPcm(new short[160]);
                        }
                    }
                    else
                    {
                        // underflow -> small silence
                        _player.AddPcm(new short[160]);
                    }
                    // wait a bit to match real-time pacing (~20ms per frame)
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
            _vocoder.Dispose();
        }
    }
}