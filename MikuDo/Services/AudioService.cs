using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MikuDo.Services;

public class AudioService : IDisposable
{
    private WasapiCapture? _capture;
    private MemoryStream? _rawStream;
    private WaveFormat? _captureFormat;
    private bool _isRecording;

    /// <summary>Completed with the clip once a stop asked for through <see cref="StopAsync"/> lands.</summary>
    private TaskCompletionSource<byte[]>? _stopped;

    public bool IsRecording => _isRecording;

    /// <summary>
    /// Whether the clip is brought to listening level before it is handed
    /// over. A clip that is stored is kept as recorded and leveled when it is
    /// played instead, so it is leveled once whatever its age.
    /// </summary>
    public bool LevelsVoice { get; init; } = true;

    public event Action? RecordingStarted;
    public event Action<byte[]>? RecordingStopped;

    public void StartRecording()
    {
        if (_isRecording) return;

        try
        {
            _capture = new WasapiCapture();
            _captureFormat = _capture.WaveFormat;
            _rawStream = new MemoryStream();

            _capture.DataAvailable += (_, args) =>
            {
                _rawStream?.Write(args.Buffer, 0, args.BytesRecorded);
            };

            _capture.RecordingStopped += (_, _) =>
            {
                byte[] wavData = Array.Empty<byte>();
                try
                {
                    if (_rawStream != null && _rawStream.Length > 0 && _captureFormat != null)
                    {
                        _rawStream.Position = 0;

                        // Convert captured audio to PCM 16-bit WAV (compatible with HTML audio)
                        var pcmFormat = new WaveFormat(44100, 16, 1);

                        using var pcmMs = new MemoryStream();
                        using (var rawReader = new RawSourceWaveStream(_rawStream, _captureFormat))
                        using (var resampler = new MediaFoundationResampler(rawReader, pcmFormat))
                        {
                            resampler.ResamplerQuality = 60;

                            var buffer = new byte[4096];
                            int read;
                            while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                pcmMs.Write(buffer, 0, read);
                            }
                        }

                        var pcm = pcmMs.ToArray();
                        if (LevelsVoice) VoiceLevel.Apply(pcm, pcmFormat.SampleRate);

                        using var outputMs = new MemoryStream();
                        using (var writer = new WaveFileWriter(outputMs, pcmFormat))
                        {
                            writer.Write(pcm, 0, pcm.Length);
                        }
                        wavData = outputMs.ToArray();
                    }
                }
                catch { }

                _rawStream?.Dispose();
                _rawStream = null;
                _capture?.Dispose();
                _capture = null;
                _captureFormat = null;
                _isRecording = false;

                RecordingStopped?.Invoke(wavData);

                var waiting = _stopped;
                _stopped = null;
                waiting?.TrySetResult(wavData);
            };

            _capture.StartRecording();
            _isRecording = true;
            RecordingStarted?.Invoke();
        }
        catch
        {
            _isRecording = false;
            _rawStream?.Dispose();
            _capture?.Dispose();
        }
    }

    public void StopRecording()
    {
        if (!_isRecording || _capture == null) return;
        _capture.StopRecording();
    }

    /// <summary>
    /// Stops and completes once the clip has been converted and handed to
    /// <see cref="RecordingStopped"/>, or straight away when nothing is recording.
    /// </summary>
    /// <remarks>
    /// The capture reports its end through the synchronization context it was
    /// started on, which is the UI thread. Blocking that thread to wait would
    /// stop the report from ever arriving, so this is awaited instead.
    /// </remarks>
    public Task<byte[]> StopAsync()
    {
        if (!_isRecording || _capture == null) return Task.FromResult(Array.Empty<byte>());

        _stopped ??= new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture.StopRecording();
        return _stopped.Task;
    }

    public void Dispose()
    {
        if (_isRecording)
            _capture?.StopRecording();
        _rawStream?.Dispose();
        _capture?.Dispose();
    }
}
