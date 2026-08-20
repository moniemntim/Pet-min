using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

/// Bounded, thread-safe bridge from EventSub notifications to the UI dispatcher.
public sealed class TwitchFeedQueue
{
    public const int Capacity = 20;
    private readonly Queue<PetFoodType> _items = new();
    private readonly object _gate = new();
    public int Count { get { lock (_gate) return _items.Count; } }
    public bool EnqueuePellet() { lock (_gate) { if (_items.Count >= Capacity) return false; _items.Enqueue(PetFoodType.Pellet); return true; } }
    public bool TryStartFeeding(Func<PetFoodType, bool> tryStart)
    {
        lock (_gate)
        {
            if (_items.Count == 0) return false;
            var food = _items.Peek();
            if (!tryStart(food)) return false;
            _items.Dequeue();
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }
}
