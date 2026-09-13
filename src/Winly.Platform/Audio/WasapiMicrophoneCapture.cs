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

    /// <summary>
    /// How long the recorder gets to hand over the frames it had already captured when the key came
    /// up. Only reached when something is wrong — a device removed mid-capture never raises
    /// <c>RecordingStopped</c> at all, and waiting on it forever would hang the turn.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private ActiveCapture? _active;

    private sealed record ActiveCapture(WasapiRecorder Recorder, Stream Destination, TaskCompletionSource Drained)
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
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WasapiRecorder recorder;
            try
            {
                // MMCSS, because the loudest thing this app does happens while the microphone is
                // open: every display is captured and JPEG-encoded in parallel with the hold, on
                // purpose (CompanionOrchestrator). A capture thread that misses its wakeup under
                // that load drops whole packets, and a dropped packet in fast speech is a dropped
                // syllable — which reads as "it can't keep up when I talk quickly" rather than as
                // the scheduling problem it is.
                recorder = new WasapiRecorderBuilder()
                    .WithFormat(TargetFormat)
                    .WithMmcssThreadPriority("Audio")
                    .Build();
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
                recorder.RecordingStopped += (_, _) =>
                {
                    destination.Dispose();
                    drained.TrySetResult();
                };
                recorder.StartRecording();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                destination.Dispose();
                throw new MicrophoneUnavailableException("The microphone is unavailable or in use by another application.", exception);
            }

            var active = new ActiveCapture(recorder, destination, drained);
            active.MaxDurationCutoff = new Timer(_ => _ = StopIfCurrent(active), null, MaxCaptureDuration, Timeout.InfiniteTimeSpan);
            _active = active;
            return Task.FromResult(pipe.Reader.AsStream());
        }
    }

    public Task StopCapture() => StopIfCurrent(_active);

    private async Task StopIfCurrent(ActiveCapture? expected)
    {
        ActiveCapture active;
        lock (_gate)
        {
            if (expected is null || _active != expected)
            {
                return;
            }

            active = expected;
            _active = null;
        }

        try
        {
            active.MaxDurationCutoff?.Dispose();
            active.Recorder.StopRecording();

            // StopRecording only asks the capture thread to finish; the frames WASAPI had already
            // captured but not yet handed over arrive on the way out. Tearing the stream down here
            // instead of waiting for them is what clipped the end of a fast utterance: someone who
            // releases the key on their last syllable loses it, the packet lands on a disposed
            // stream and is swallowed as "a packet raced the stop". Costs a buffer period at most,
            // and only on the way to a transcript that now has the last word in it.
            await active.Drained.Task.WaitAsync(DrainTimeout);
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
    }
}
