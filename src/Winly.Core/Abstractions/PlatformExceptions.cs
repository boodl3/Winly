namespace Winly.Core.Abstractions;

public sealed class ActivationKeyUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class MicrophoneUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class DisplayCaptureUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PlaybackDeviceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
