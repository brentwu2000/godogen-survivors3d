using Godot;

/// Backpack contents for one run.
///
/// Capacity is measured in bulk rather than item count, so the interesting
/// decision — drop the bulky scrap to fit the small valuable thing — exists from
/// the start rather than being retrofitted onto a slot grid later.
public sealed class Inventory
{
    public int Capacity { get; }
    public int UsedBulk { get; private set; }
    public int FreeBulk => Capacity - UsedBulk;

    private readonly ItemResource[] _items;
    private readonly int[] _counts;

    public int EntryCount { get; private set; }

    public Inventory(int capacity, int maxEntries = 32)
    {
        Capacity = capacity;
        _items = new ItemResource[maxEntries];
        _counts = new int[maxEntries];
    }

    public ItemResource ItemAt(int index) => _items[index];
    public int CountAt(int index) => _counts[index];

    /// Adds as much of the stack as fits and returns how many were taken. A
    /// partial take is deliberate: a full backpack should still skim the top of
    /// a container rather than refusing it outright.
    public int TryAdd(ItemResource item, int count)
    {
        if (item.Bulk <= 0)
            return 0;

        int room = FreeBulk / item.Bulk;
        int taken = Mathf.Min(count, room);
        if (taken <= 0)
            return 0;

        for (int i = 0; i < EntryCount; i++)
        {
            if (_items[i] == item)
            {
                _counts[i] += taken;
                UsedBulk += taken * item.Bulk;
                return taken;
            }
        }

        if (EntryCount >= _items.Length)
            return 0;

        _items[EntryCount] = item;
        _counts[EntryCount] = taken;
        EntryCount++;
        UsedBulk += taken * item.Bulk;
        return taken;
    }

    public int TotalValue
    {
        get
        {
            int total = 0;
            for (int i = 0; i < EntryCount; i++)
                total += _items[i].Value * _counts[i];
            return total;
        }
    }

    /// Index of the entry worth most per unit, or -1 when empty. This is what
    /// "secure the best thing" means under pressure: raw value, not value per
    /// bulk, because the safe box is small enough that bulk rarely decides.
    /// Index of the entry worth most, or -1 when empty.
    ///
    /// `prefer` names entries that jump the queue: if anything in the bag matches
    /// it, the answer comes from among those and value only breaks the tie. It
    /// exists for the collection — a wedding ring is worth almost nothing and is
    /// the one thing in the bag that cannot be bought again, so "most valuable"
    /// is the wrong question to ask about it.
    public int MostValuableIndex(System.Func<ItemResource, bool>? prefer = null)
    {
        int best = Scan(prefer, wanted: true);
        return best >= 0 ? best : Scan(prefer, wanted: false);
    }

    private int Scan(System.Func<ItemResource, bool>? prefer, bool wanted)
    {
        // A null filter has nothing to prefer, so the first pass is skipped
        // entirely rather than matching everything and making the second dead.
        if (wanted && prefer == null)
            return -1;

        int best = -1;
        int bestValue = -1;

        for (int i = 0; i < EntryCount; i++)
        {
            if (_counts[i] <= 0)
                continue;

            if (wanted && !prefer!(_items[i]))
                continue;

            if (_items[i].Value <= bestValue)
                continue;

            bestValue = _items[i].Value;
            best = i;
        }

        return best;
    }

    /// Index of the entry worth least *per unit of bulk*, or -1 when empty.
    ///
    /// Per bulk, unlike `MostValuableIndex`, and the asymmetry is the point.
    /// Securing asks "what do I most want to keep", where the safe box is small
    /// enough that bulk rarely decides. Dropping asks "what is costing me the
    /// most room for the least return", which is a different question with a
    /// different answer: four boxes of rifle rounds are worth more in total than
    /// one circuit board and are exactly what should go over the side when a
    /// cache is on the ground in front of you.
    /// `protect` names entries to leave alone. If *everything* is protected the
    /// filter is dropped rather than the call failing — a bag full of curiosities
    /// with a cache on the floor in front of it still has to be able to make
    /// room, and a key that silently did nothing under pressure is worse than one
    /// that costs something.
    public int LeastValuableIndex(System.Func<ItemResource, bool>? protect = null)
    {
        int index = Cheapest(protect);
        return index >= 0 ? index : Cheapest(null);
    }

    private int Cheapest(System.Func<ItemResource, bool>? protect)
    {
        int worst = -1;
        float worstRate = float.MaxValue;

        for (int i = 0; i < EntryCount; i++)
        {
            if (_counts[i] <= 0 || _items[i].Bulk <= 0)
                continue;

            if (protect != null && protect(_items[i]))
                continue;

            float rate = _items[i].Value / (float)_items[i].Bulk;
            if (rate >= worstRate)
                continue;

            worstRate = rate;
            worst = i;
        }

        return worst;
    }

    /// Removes a single unit, collapsing the entry when it empties.
    public bool RemoveOne(int index)
    {
        if (index < 0 || index >= EntryCount || _counts[index] <= 0)
            return false;

        _counts[index]--;
        UsedBulk -= _items[index].Bulk;

        if (_counts[index] > 0)
            return true;

        int last = --EntryCount;
        _items[index] = _items[last];
        _counts[index] = _counts[last];
        _items[last] = null!;
        _counts[last] = 0;
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < EntryCount; i++)
            _items[i] = null!;

        EntryCount = 0;
        UsedBulk = 0;
    }
}
