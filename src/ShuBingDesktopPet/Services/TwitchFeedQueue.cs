using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

/// Bounded, thread-safe bridge from EventSub notifications to the UI dispatcher.
public sealed class TwitchFeedQueue
{
    public const int Capacity = 20;
    private sealed class FeedItem(PetFoodType food)
    {
        public PetFoodType Food { get; } = food;
    }

    private readonly LinkedList<FeedItem> _items = new();
    private readonly object _gate = new();
    private FeedItem? _processing;

    private int CountUnsafe()
    {
        if (_processing is null) return _items.Count;

        var node = _items.First;
        while (node is not null)
        {
            if (ReferenceEquals(node.Value, _processing)) return _items.Count;
            node = node.Next;
        }

        return _items.Count + 1;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return CountUnsafe();
            }
        }
    }

    public bool EnqueuePellet()
    {
        lock (_gate)
        {
            if (CountUnsafe() >= Capacity) return false;
            _items.AddLast(new FeedItem(PetFoodType.Pellet));
            return true;
        }
    }

    public bool TryStartFeeding(Func<PetFoodType, bool> tryStart)
    {
        FeedItem item;
        lock (_gate)
        {
            if (_processing is not null || _items.First is null) return false;
            item = _items.First.Value;
            _processing = item;
        }

        var started = false;
        try
        {
            // The callback may synchronously enqueue or clear the feed queue.
            started = tryStart(item.Food);
            return started;
        }
        finally
        {
            lock (_gate)
            {
                if (started)
                {
                    // Remove only the item observed before the callback. Clear may
                    // have removed it, and a later enqueue must not be consumed.
                    var node = _items.First;
                    while (node is not null)
                    {
                        if (ReferenceEquals(node.Value, item))
                        {
                            _items.Remove(node);
                            break;
                        }

                        node = node.Next;
                    }
                }

                _processing = null;
            }
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
