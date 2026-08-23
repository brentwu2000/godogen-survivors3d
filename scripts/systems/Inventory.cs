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
    public int MostValuableIndex()
    {
        int best = -1;
        int bestValue = 0;

        for (int i = 0; i < EntryCount; i++)
        {
            if (_counts[i] > 0 && _items[i].Value > bestValue)
            {
                bestValue = _items[i].Value;
                best = i;
            }
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
    public int LeastValuableIndex()
    {
        int worst = -1;
        float worstRate = float.MaxValue;

        for (int i = 0; i < EntryCount; i++)
        {
            if (_counts[i] <= 0 || _items[i].Bulk <= 0)
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
