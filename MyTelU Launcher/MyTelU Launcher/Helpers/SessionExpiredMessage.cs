namespace MyTelU_Launcher.Helpers;

/// <summary>
/// Sent when a live session validation returns NoSession but a cached schedule
/// is available to keep showing. The View handles this by prompting the user to
/// relog or stay on the cached content.
/// </summary>
public class SessionExpiredMessage { }
