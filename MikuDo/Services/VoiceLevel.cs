using System.IO;
using NAudio.Wave;

namespace MikuDo.Services;

/// <summary>
/// Brings a voice clip up to a comfortable listening level. It measures how
/// loud the speech is, not how high the single tallest sample reaches, so one
/// click or pop in the clip cannot hold the voice down. A limiter then keeps
/// the lifted peaks under full scale instead of letting them clip.
/// </summary>
/// <remarks>
/// Microphones vary hugely in level and Windows applies no gain of its own on
/// the way in, so a normal speaking voice often arrives 30 dB or more below
/// where it should be. A clip already at the target comes back untouched.
/// The gain is capped, so a clip leveled once can still gain more from a
/// second pass: level a clip once, at the point it is played.
/// </remarks>
public static class VoiceLevel
{
    /// <summary>Where the speech itself should sit: about -18 dBFS RMS, a typical voice-memo level.</summary>
    private const float TargetSpeech = 0.126f;

    /// <summary>The highest any sample may reach after leveling: about -1 dBFS.</summary>
    private const float Ceiling = 0.89f;

    /// <summary>At most +30 dB.</summary>
    private const float MaxGain = 31.6f;

    /// <summary>
    /// At most +24 dB when the clip barely rises above its own background,
    /// so a near-silent clip stays quiet instead of becoming loud hiss.
    /// </summary>
    private const float MaxGainOverNoise = 16f;

    /// <summary>Below about -70 dBFS there is no voice to find.</summary>
    private const float Silence = 0.0003f;

    /// <summary>Gains closer to 1 than this (under 1 dB) are not worth rewriting the clip for.</summary>
    private const float Negligible = 1.12f;

    /// <summary>
    /// Levels 16-bit mono PCM in place.
    /// </summary>
    /// <returns>False when the clip was left as it was.</returns>
    public static bool Apply(byte[] pcm, int sampleRate)
    {
        var count = pcm.Length / 2;
        if (count == 0 || sampleRate <= 0) return false;

        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;

        RemoveRumble(samples, sampleRate);

        var gain = GainFor(samples, sampleRate);
        if (gain < Negligible) return false;

        for (var i = 0; i < count; i++) samples[i] *= gain;
        Limit(samples, sampleRate);

        for (var i = 0; i < count; i++)
        {
            var value = (int)MathF.Round(samples[i] * 32767f);
            value = Math.Clamp(value, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return true;
    }

    /// <summary>
    /// A whole WAV file, leveled. Anything other than 16-bit mono PCM, or a
    /// file that cannot be read, comes back as it was.
    /// </summary>
    public static byte[] ApplyToWav(byte[] wav)
    {
        if (wav.Length == 0) return wav;
        try
        {
            WaveFormat format;
            byte[] pcm;
            using (var reader = new WaveFileReader(new MemoryStream(wav)))
            {
                format = reader.WaveFormat;
                if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels != 1)
                    return wav;

                pcm = new byte[reader.Length];
                var read = 0;
                while (read < pcm.Length)
                {
                    var n = reader.Read(pcm, read, pcm.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < pcm.Length) Array.Resize(ref pcm, read);
            }

            if (!Apply(pcm, format.SampleRate)) return wav;

            using var output = new MemoryStream();
            using (var writer = new WaveFileWriter(output, format))
                writer.Write(pcm, 0, pcm.Length);
            return output.ToArray();
        }
        catch
        {
            return wav;
        }
    }

    /// <summary>
    /// Takes out what sits below the voice: a DC offset and rumble under
    /// about 80 Hz, which would otherwise use up headroom the voice needs.
    /// </summary>
    private static void RemoveRumble(float[] samples, int sampleRate)
    {
        var rc = 1.0 / (2 * Math.PI * 80);
        var a = (float)(rc / (rc + 1.0 / sampleRate));
        float previousIn = samples[0], previousOut = 0;
        samples[0] = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            var input = samples[i];
            previousOut = a * (previousOut + input - previousIn);
            previousIn = input;
            samples[i] = previousOut;
        }
    }

    /// <summary>
    /// The gain that puts the speech at <see cref="TargetSpeech"/>. Speech is
    /// the 20 ms stretches within 30 dB of the loudest ones; the pauses
    /// between words are left out, or they would drag the measure down and
    /// the gain up.
    /// </summary>
    private static float GainFor(float[] samples, int sampleRate)
    {
        var window = Math.Max(1, sampleRate / 50);
        var levels = new List<float>(samples.Length / window + 1);
        for (var start = 0; start + window <= samples.Length; start += window)
        {
            double energy = 0;
            for (var i = start; i < start + window; i++) energy += samples[i] * (double)samples[i];
            levels.Add((float)Math.Sqrt(energy / window));
        }
        if (levels.Count == 0) return 1f;

        levels.Sort();
        var loud = levels[(int)(levels.Count * 0.95)];
        if (loud < Silence) return 1f;

        var gate = Math.Max(loud * 0.0316f, Silence);
        double speechEnergy = 0;
        var speechWindows = 0;
        foreach (var level in levels)
        {
            if (level < gate) continue;
            speechEnergy += level * (double)level;
            speechWindows++;
        }
        if (speechWindows == 0) return 1f;

        var speech = (float)Math.Sqrt(speechEnergy / speechWindows);
        var background = Math.Max(levels[levels.Count / 10], Silence);
        var cap = speech / background < 3.16f ? MaxGainOverNoise : MaxGain;

        return Math.Clamp(TargetSpeech / speech, 1f, cap);
    }

    /// <summary>
    /// Holds every sample under <see cref="Ceiling"/>. The gain eases down
    /// over the 5 ms before a peak and recovers over about 80 ms after it,
    /// so the peaks are tamed without the click a hard clip would leave.
    /// </summary>
    private static void Limit(float[] samples, int sampleRate)
    {
        var count = samples.Length;
        var gains = new float[count];
        for (var i = 0; i < count; i++)
        {
            var magnitude = Math.Abs(samples[i]);
            gains[i] = magnitude > Ceiling ? Ceiling / magnitude : 1f;
        }

        // Backwards, so the gain is already falling when a peak arrives.
        var attackStep = 1f / Math.Max(1, sampleRate / 200);
        for (var i = count - 2; i >= 0; i--)
            gains[i] = Math.Min(gains[i], gains[i + 1] + attackStep);

        // Forwards, so it climbs back slowly instead of snapping open.
        var release = 1f - MathF.Exp(-1f / (0.08f * sampleRate));
        var current = gains[0];
        for (var i = 0; i < count; i++)
        {
            current = Math.Min(gains[i], current + (1f - current) * release);
            samples[i] *= current;
        }
    }
}
