using System;
using System.Collections.Generic;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Buffer circulaire pour l'historique des mesures.
    /// Stocke les N dernières valeurs (CPU, RAM, GPU, Temp CPU).
    /// </summary>
    public class HistoryBuffer
    {
        private readonly int _capacity;
        private readonly Queue<HistoryPoint> _points;

        public HistoryBuffer(int capacity = 60)
        {
            _capacity = capacity;
            _points = new Queue<HistoryPoint>(capacity);
        }

        public void Add(HistoryPoint point)
        {
            if (_points.Count >= _capacity)
                _points.Dequeue();

            _points.Enqueue(point);
        }

        public List<HistoryPoint> GetAll()
        {
            return new List<HistoryPoint>(_points);
        }

        public void Clear()
        {
            _points.Clear();
        }

        public int Count => _points.Count;
    }

    public class HistoryPoint
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double RamUsage { get; set; }
        public double? CpuTemp { get; set; }
        public double? GpuUsage { get; set; }
        public double? GpuTemp { get; set; }
    }
}