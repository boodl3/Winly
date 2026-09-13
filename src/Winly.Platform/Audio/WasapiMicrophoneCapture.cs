using System.IO.Pipelines;
using NAudio.Wave;
using Serilog;
using Winly.Core.Abstractions;

namespace Winly.Platform.Audio;

/// <summary>
/// Captures the default microphone via WASAPI shared mode, asking the audio engine itself for
/// 16 kHz mono PCM16 (AutoConvertPcm) so no resampler is needed. The device is opened per
/// activation and released on stop (FR-032); capture ends after 30 s regardless (FR-004).
/// </summary>
public sealed class WasapiMicrophoneCapture : IMicrophoneCapture
{
    public static readonly WaveFormat TargetFormat = new(16000, 16, 1);
    public static readonly TimeSpan MaxCaptureDuration = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private ActiveCapture? _active;

    private sealed record ActiveCapture(WasapiRecorder Recorder, Stream Destination)
    {
        public Timer? MaxDurationCutoff { get; set; }
    }

    public Task<Stream> StartCapture(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("Microphone capture is already running.");
            }

            var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
            var destination = pipe.Writer.AsStream();
            WasapiRecorder recorder;
            try
            {
                recorder = new WasapiRecorderBuilder().WithFormat(TargetFormat).Build();
                recorder.DataAvailable += (buffer, _, _, _) =>
                {
                    try
                    {
                        destination.Write(buffer);
                    }
                    catch (ObjectDisposedException)
                    {
                        // A packet raced the stop; the capture is already over.
                    }
                };
                recorder.RecordingStopped += (_, _) => destination.Dispose();
                recorder.StartRecording();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                destination.Dispose();
                throw new MicrophoneUnavailableException("The microphone is unavailable or in use by another application.", exception);
            }

            var active = new ActiveCapture(recorder, destination);
            active.MaxDurationCutoff = new Timer(_ => _ = StopIfCurrent(active), null, MaxCaptureDuration, Timeout.InfiniteTimeSpan);
            _active = active;
            return Task.FromResult(pipe.Reader.AsStream());
        }
    }

    public Task StopCapture() => StopIfCurrent(_active);

    private Task StopIfCurrent(ActiveCapture? expected)
    {
        ActiveCapture active;
        lock (_gate)
        {
            if (expected is null || _active != expected)
            {
                return Task.CompletedTask;
            }

            active = expected;
            _active = null;
        }

        try
        {
            active.MaxDurationCutoff?.Dispose();
            active.Recorder.StopRecording();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Microphone capture did not stop cleanly");
        }
        finally
        {
            active.Recorder.Dispose();
            active.Destination.Dispose();
        }

        return Task.CompletedTask;
    }
}
