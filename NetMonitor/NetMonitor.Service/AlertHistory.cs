namespace NetMonitor.Service;

public sealed class AlertHistory
{
    private readonly object _lock = new();
    private readonly Queue<Alert> _items = new();
    public int Capacity { get; }

    public AlertHistory(int capacity = 500) => Capacity = capacity;

    public void Add(Alert a)
    {
        lock (_lock)
        {
            _items.Enqueue(a);
            while (_items.Count > Capacity) _items.Dequeue();
        }
    }

    public IReadOnlyList<Alert> Snapshot()
    {
        lock (_lock) return _items.Reverse().ToArray();
    }
}