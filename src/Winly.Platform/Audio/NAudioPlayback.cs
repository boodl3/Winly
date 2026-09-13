using NAudio.Wave;
using Serilog;
using Winly.Core.Abstractions;
using Winly.Core.Providers;

namespace Winly.Platform.Audio;

/// <summary>
/// Plays what <c>/tts</c> returned through the default output device. Raw PCM plays straight off
/// the network stream as it arrives; MP3 has to be buffered whole before NAudio can index it.
/// </summary>
public sealed class NAudioPlayback : IAudioPlayback
{
    // 3 x 50 ms: playback starts on 150 ms of audio rather than the 300 ms default.
    private const int BufferMilliseconds = 50;
    private const int BufferCount = 3;

    private readonly object _gate = new();
    private WaveOut? _output;
    private IDisposable? _source;

    public async Task Play(SpokenAudio audio, CancellationToken cancellationToken)
    {
        await Stop();

        var (provider, source) = CreateSource(audio);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WaveOut output;
        try
        {
            output = new WaveOut { BufferMilliseconds = BufferMilliseconds, NumberOfBuffers = BufferCount };
            output.Init(provider);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            source.Dispose();
            throw new PlaybackDeviceUnavailableException("No playback device is available.", exception);
        }

        output.PlaybackStopped += (_, args) =>
        {
            // A torn-down stream faults the read that was in flight; that is a stop, not a device failure.
            if (args.Exception is null || cancellationToken.IsCancellationRequested)
            {
                finished.TrySetResult();
            }
            else
            {
                finished.TrySetException(new PlaybackDeviceUnavailableException("Playback failed.", args.Exception));
            }
        };

        lock (_gate)
        {
            _output = output;
            _source = source;
        }

        using var cancellation = cancellationToken.Register(() => _ = Stop());
        try
        {
            output.Play();
            await finished.Task;
        }
        finally
        {
            await Stop();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task Stop()
    {
        WaveOut? output;
        IDisposable? source;
        lock (_gate)
        {
            output = _output;
            source = _source;
            _output = null;
            _source = null;
        }

        try
        {
            output?.Stop();
            output?.Dispose();
            source?.Dispose();
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Playback did not stop cleanly");
        }

        return Task.CompletedTask;
    }

    private static (IWaveProvider Provider, IDisposable Source) CreateSource(SpokenAudio audio)
    {
        if (audio.PcmSampleRateHz is not int sampleRate)
        {
            // Mp3FileReader indexes the whole file up front, so this path cannot stream at all.
            var buffered = new MemoryStream();
            using (audio.Audio)
            {
                audio.Audio.CopyTo(buffered);
            }

            buffered.Position = 0;
            var reader = new Mp3FileReader(buffered);
            return (reader, reader);
        }

        var streamed = new StreamedPcmProvider(audio.Audio, new WaveFormat(sampleRate, 16, 1));
        return (streamed, streamed);
    }

    /// <summary>
    /// Feeds NAudio straight from the response body. Reads block until bytes arrive, which is what
    /// lets playback begin before synthesis has finished; a zero-length read is the end of the clip.
    /// </summary>
    private sealed class StreamedPcmProvider(Stream source, WaveFormat format) : IWaveProvider, IDisposable
    {
        public WaveFormat WaveFormat => format;

        public int Read(Span<byte> buffer)
        {
            // NAudio zero-pads a short read, which is an audible gap. The response stream blocks
            // until bytes arrive, so filling the buffer here is what keeps the answer continuous;
            // only a genuine end of stream returns less than was asked for.
            var filled = 0;
            while (filled < buffer.Length)
            {
                var read = source.Read(buffer[filled..]);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            return filled;
        }

        public void Dispose() => source.Dispose();
    }
}
