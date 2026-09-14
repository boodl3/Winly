using NAudio.Wave;
using Serilog;
using Winly.Core.Abstractions;
using Winly.Core.Providers;

namespace Winly.Platform.Audio;

/// <summary>
/// Plays what <c>/tts</c> returned through the default output device.
/// </summary>
/// <remarks>
/// <para>
/// One output device is created for the life of the process and every clip is appended to its
/// buffer. It used to be a fresh <see cref="WaveOut"/> per clip, and because an answer is
/// synthesised a sentence at a time, that meant opening and closing the audio device between
/// every sentence of every answer. That one decision produced three separate symptoms that each
/// looked like its own bug:
/// </para>
/// <list type="bullet">
/// <item>a gap at every sentence boundary, because a new device has to buffer 150 ms before it
/// makes a sound;</item>
/// <item>the first syllable of an answer clipped, because closing the outgoing device contended
/// with the incoming one starting;</item>
/// <item>a device leaked on every superseded playback, because the guard that stopped an
/// abandoned answer tearing down its replacement also skipped disposing its own.</item>
/// </list>
/// <para>
/// Measured before this change, over ~35 activations: idle CPU 4.2 % to 12.6 % of a core, +378
/// handles, +15 threads. Keeping one device removes the cause rather than guarding each symptom.
/// </para>
/// <para>
/// Ownership is a generation counter rather than a reference check. The device is shared, so
/// "stop" from a new activation must silence the old answer without the old answer's own teardown
/// then silencing the new one — the same race <c>StopIfCurrent</c> existed for, expressed once
/// here instead of at each call site. A <see cref="Play"/> whose generation has moved on returns
/// without touching the buffer.
/// </para>
/// </remarks>
public sealed class NAudioPlayback : IAudioPlayback, IDisposable
{
    // 3 x 50 ms: playback starts on 150 ms of audio rather than NAudio's 300 ms default.
    private const int DeviceBufferMilliseconds = 50;
    private const int DeviceBufferCount = 3;

    // How far ahead of the speakers the pump is allowed to get. Without a ceiling a long answer
    // would buffer the whole of itself, and abandoning it would then have to discard seconds of
    // audio that had already been paid for.
    private static readonly TimeSpan MaxBufferedAhead = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    private readonly object _gate = new();
    private WaveOut? _device;
    private BufferedWaveProvider? _buffer;
    private WaveFormat? _format;
    private int _playing;
    private long _generation;

    public async Task Play(SpokenAudio audio, CancellationToken cancellationToken)
    {
        var (source, format) = OpenSource(audio);
        try
        {
            long mine;
            BufferedWaveProvider buffer;
            lock (_gate)
            {
                buffer = EnsureDevice(format);
                mine = _generation;
                _playing++;
            }

            try
            {
                await Pump(source, buffer, mine, cancellationToken);
                await Drain(buffer, mine, cancellationToken);
            }
            finally
            {
                Quiesce();
            }
        }
        finally
        {
            source.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Idempotent; must not throw. Silences whatever is playing, whoever started it.</summary>
    public Task Stop()
    {
        lock (_gate)
        {
            // Bumping the generation is what tells every in-flight Play that it no longer owns the
            // device, so their teardown leaves the next answer's audio alone.
            _generation++;
            _buffer?.ClearBuffer();

            // ClearBuffer only empties our own provider. The driver is already holding up to
            // NumberOfBuffers x BufferMilliseconds — 150 ms — of the outgoing answer, and it plays
            // that out regardless: a short burst of the abandoned answer before the new one starts,
            // which sounds like the app trying to say something and changing its mind. Disposing
            // the device used to discard those buffers as a side effect; keeping one device means
            // asking for it. Stop() calls waveOutReset, and the next EnsureDevice resumes with
            // Play() on the same device.
            try
            {
                _device?.Stop();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Playback did not flush cleanly");
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _generation++;
            try
            {
                _device?.Dispose();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Playback device did not close cleanly");
            }

            _device = null;
            _buffer = null;
            _format = null;
        }
    }

    /// <summary>Feeds the device from the response body, stopping early if a newer answer took over.</summary>
    private async Task Pump(Stream source, BufferedWaveProvider buffer, long mine, CancellationToken cancellationToken)
    {
        var chunk = new byte[4096];
        while (true)
        {
            // Reads block until bytes arrive, which is what lets an answer start playing before
            // synthesis has finished. A zero-length read is the end of this clip.
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return;
            }

            while (true)
            {
                lock (_gate)
                {
                    if (_generation != mine)
                    {
                        return; // Superseded: the buffer belongs to a newer answer now.
                    }

                    if (buffer.BufferedDuration < MaxBufferedAhead)
                    {
                        buffer.AddSamples(chunk, 0, read);
                        break;
                    }
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
    }

    /// <summary>Returns once the speakers have caught up with what was queued.</summary>
    private async Task Drain(BufferedWaveProvider buffer, long mine, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_generation != mine || buffer.BufferedBytes == 0)
                {
                    return;
                }
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Pauses the device once nothing is playing. The device and its handle are kept — reopening
    /// them is the cost this class exists to avoid — but a paused device stops consuming buffers,
    /// so an idle Winly holds no running audio thread (constitution: no measurable CPU at idle).
    /// </summary>
    private void Quiesce()
    {
        lock (_gate)
        {
            if (--_playing > 0 || _device is null)
            {
                return;
            }

            try
            {
                _buffer?.ClearBuffer();
                _device.Pause();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Playback did not pause cleanly");
            }
        }
    }

    /// <summary>Creates the device on first use, or replaces it if the audio format has changed.</summary>
    private BufferedWaveProvider EnsureDevice(WaveFormat format)
    {
        if (_device is not null && _buffer is not null && _format is not null && _format.Equals(format))
        {
            _device.Play(); // No-op when already playing; resumes after Quiesce paused it.
            return _buffer;
        }

        try
        {
            _device?.Dispose();
            var buffer = new BufferedWaveProvider(format)
            {
                // Silence rather than a stop when the pump falls behind: an underrun mid-answer
                // would otherwise end playback and need restarting, which is the gap all over again.
                ReadFully = true,
                // Capacity is fixed at NAudio's default 5 s and is not settable in this version.
                // The pump never queues more than MaxBufferedAhead (2 s), so the ceiling that
                // actually binds is ours; this flag only keeps a pathological overrun from throwing.
                DiscardOnBufferOverflow = true,
            };
            var device = new WaveOut
            {
                BufferMilliseconds = DeviceBufferMilliseconds,
                NumberOfBuffers = DeviceBufferCount,
            };
            device.Init(buffer);
            device.Play();

            _device = device;
            _buffer = buffer;
            _format = format;
            return buffer;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _device = null;
            _buffer = null;
            _format = null;
            throw new PlaybackDeviceUnavailableException("No playback device is available.", exception);
        }
    }

    /// <summary>
    /// Raw PCM plays straight off the network stream as it arrives; MP3 has to be buffered whole
    /// first, because <see cref="Mp3FileReader"/> indexes the file up front and cannot stream.
    /// </summary>
    private static (Stream Source, WaveFormat Format) OpenSource(SpokenAudio audio)
    {
        if (audio.PcmSampleRateHz is int sampleRate)
        {
            return (audio.Audio, new WaveFormat(sampleRate, 16, 1));
        }

        var buffered = new MemoryStream();
        using (audio.Audio)
        {
            audio.Audio.CopyTo(buffered);
        }

        buffered.Position = 0;
        var reader = new Mp3FileReader(buffered);
        return (reader, reader.WaveFormat);
    }
}
