namespace MyTelU_Launcher.Helpers;

/// <summary>
/// Sent via WeakReferenceMessenger when the user triggers an action that requires
/// a network connection (e.g. Refresh, change academic year) while the device is offline.
/// The receiving view should present a ContentDialog explaining offline-mode limitations.
/// </summary>
public sealed class OfflineModeMessage
{
    /// <summary>Human-readable label for which action was blocked (optional).</summary>
    public string ActionLabel { get; init; } = "Refresh";
}
