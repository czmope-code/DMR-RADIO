using System;
using NAudio.Wave;

namespace DmrRadio.Audio
{
    /// <summary>
    /// Simple playback wrapper using NAudio BufferedWaveProvider.
    /// </summary>
    public class AudioPlayer : IDisposable
    {
        private readonly WaveOutEvent _waveOut;
        private readonly BufferedWaveProvider _buffer;

        public AudioPlayer()
        {
            var wf = new WaveFormat(8000, 16, 1); // 8 kHz, 16-bit, mono
            _buffer = new BufferedWaveProvider(wf)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true
            };
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_buffer);
        }

        public void Start() => _waveOut.Play();
        public void Stop() => _waveOut.Stop();

        /// <summary>
        /// Add PCM samples (short[], 16-bit) to buffer for playback.
        /// </summary>
        public void AddPcm(short[] pcm)
        {
            if (pcm == null || pcm.Length == 0) return;
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            _buffer.AddSamples(bytes, 0, bytes.Length);
        }

        public void Dispose()
        {
            try
            {
                _waveOut?.Stop();
            }
            catch { }
            _waveOut?.Dispose();
        }
    }
}