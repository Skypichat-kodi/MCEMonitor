using NetMonitor.Service.Models;

namespace NetMonitor.Service;

public sealed class AlertManager
{
    public event EventHandler<Alert>? AlertRaised;
    public AlertHistory History { get; } = new();

    public void Raise(IntrusionEvent evt)
    {
        var alert = new Alert
        {
            Timestamp = evt.Timestamp,
            Severity = evt.Severity,
            Source = evt.Source,
            Title = $"Intrusion détectée : {evt.Source}",
            Message = evt.Message,
            Details = evt.Details
        };

        History.Add(alert);
        CoreLog.Warn($"[{alert.Severity}] {alert.Title} — {alert.Message}");

        try { AlertRaised?.Invoke(this, alert); }
        catch (Exception ex) { CoreLog.Error($"AlertRaised handler : {ex.Message}"); }
    }
}