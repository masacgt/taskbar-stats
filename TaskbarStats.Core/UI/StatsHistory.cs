using TaskbarStats.Models;

namespace TaskbarStats.UI;

public sealed class StatsHistory
{
    private readonly Queue<SystemStatsSample> _samples = new();
    private readonly int _capacity;

    public StatsHistory(int capacity = 300) => _capacity = capacity;

    public int Count => _samples.Count;

    public void Add(SystemStatsSample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > _capacity)
        {
            _samples.Dequeue();
        }
    }

    public IReadOnlyList<SystemStatsSample> Snapshot() => _samples.ToList();
}
