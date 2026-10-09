using System.IO;
using NAudio.Wave;

namespace MikuDo.Services;

/// <summary>Plays one voice memo at a time and reports where it is.</summary>
public class AudioPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private WaveFileReader? _reader;
    private MemoryStream? _stream;

    public string? CurrentId { get; private set; }
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>Raised on the playback thread when a clip reaches its end.</summary>
    public event Action? PlaybackEnded;

    public void Toggle(string id, byte[] wavData)
    {
        if (CurrentId == id && _output != null)
        {
            if (_output.PlaybackState == PlaybackState.Playing) _output.Pause();
            else _output.Play();
            return;
        }
        Play(id, wavData);
    }

    public void Play(string id, byte[] wavData)
    {
        Stop();
        try
        {
            _stream = new MemoryStream(wavData);
            _reader = new WaveFileReader(_stream);
            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.PlaybackStopped += (_, _) => PlaybackEnded?.Invoke();
            _output.Play();
            CurrentId = id;
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        try { _output?.Stop(); } catch { }
        _output?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _output = null;
        _reader = null;
        _stream = null;
        CurrentId = null;
    }

    /// <summary>Peak amplitude per bucket, 0..1, for drawing a waveform.</summary>
    public static double[] Waveform(byte[] wavData, int buckets)
    {
        var bars = new double[buckets];
        try
        {
            using var ms = new MemoryStream(wavData);
            using var reader = new WaveFileReader(ms);

            var total = reader.SampleCount;
            if (total <= 0) return Flat(buckets);

            var perBucket = Math.Max(1, total / buckets);
            var index = 0;
            var peak = 0f;
            var read = 0L;

            while (reader.Position < reader.Length && index < buckets)
            {
                var frame = reader.ReadNextSampleFrame();
                if (frame == null || frame.Length == 0) break;

                peak = Math.Max(peak, Math.Abs(frame[0]));
                if (++read >= perBucket)
                {
                    bars[index++] = Math.Min(1.0, peak);
                    peak = 0f;
                    read = 0;
                }
            }

            var loudest = bars.Max();
            if (loudest <= 0.001) return Flat(buckets);
            for (var i = 0; i < buckets; i++) bars[i] = Math.Clamp(bars[i] / loudest, 0.12, 1.0);
            return bars;
        }
        catch
        {
            return Flat(buckets);
        }
    }

    private static double[] Flat(int buckets)
        => Enumerable.Range(0, buckets)
            .Select(i => 0.2 + Math.Abs(Math.Sin(i * 0.7)) * 0.7)
            .ToArray();

    public void Dispose() => Stop();
}
