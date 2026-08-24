using Godot;

/// Checks that the survivors are three ways to play rather than three difficulty
/// settings.
///
///   godot --headless --script test/CharacterProbe.cs
///
/// The obvious assertion — "the numbers differ" — is worthless, because any three
/// sets of numbers differ. Two things have to hold instead, and they pull against
/// each other:
///
///   nothing is strictly better than the Drifter, or the roster is a ladder and
///   the choice is which rung to stand on
///
///   the differences are large enough to be *felt* rather than read, or the
///   choice is a preference and the player will pick by colour
///
/// The third is quieter and matters more than either: the Drifter has to still be
/// the survivor eleven phases of balance work were tuned against, to the digit.
/// Every number in the shop, every enemy in the table and every probe in this
/// folder was measured against one hundred health, six metres a second and twenty
/// of bulk. A roster whose default "improved" on that would have re-balanced the
/// game as a side effect of adding a menu.
public partial class CharacterProbe : SceneTree
{
    private bool _failed;

    public override void _Initialize()
    {
        Stage("the roster loads and the Drifter is untouched", StageDefaultUnchanged);
        Stage("nothing is strictly better than anything else", StageNoStrictlyBetter);
        Stage("the differences are big enough to feel", StageDifferencesAreReal);
        Stage("an ability is a head start, not a second economy", StageAbilitiesAreModifiers);
        Stage("later survivors cost extractions to open", StageUnlockOrder);

        GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
        Quit(_failed ? 1 : 0);
    }

    private void Stage(string label, System.Func<bool> stage)
    {
        bool ok;
        try
        {
            ok = stage();
        }
        catch (System.Exception error)
        {
            GD.PushError($"  {error.Message}");
            ok = false;
        }

        GD.Print($"{label}: {(ok ? "ok" : "FAILED")}");
        _failed |= !ok;
    }

    /// The numbers `Player` shipped with, written here rather than read from the
    /// resource.
    ///
    /// Duplicated on purpose, and it is the only duplication in this file. Reading
    /// them from `drifter.tres` would compare the table against itself and pass
    /// for any value at all; the point of this stage is that the table agrees with
    /// what the *rest of the game* was tuned against, and the only way to say that
    /// is to write it down.
    private const float DrifterHealth = 100.0f;
    private const float DrifterSpeed = 6.0f;
    private const int DrifterBulk = 20;

    private bool StageDefaultUnchanged()
    {
        CharacterResource[] all = CharacterBook.All;
        if (all.Length < 2)
        {
            GD.PushError($"  only {all.Length} survivor(s) — run BuildCharacters.cs");
            return false;
        }

        CharacterResource first = all[0];
        bool ok = true;

        if (Mathf.Abs(first.MaxHealth - DrifterHealth) > 0.01f
            || Mathf.Abs(first.MoveSpeed - DrifterSpeed) > 0.01f
            || first.CarryCapacity != DrifterBulk)
        {
            GD.PushError($"  {first.CharacterName} is {first.MaxHealth:F0} hp / "
                       + $"{first.MoveSpeed:F1} m/s / {first.CarryCapacity} bulk, and the game was "
                       + $"balanced against {DrifterHealth:F0} / {DrifterSpeed:F1} / {DrifterBulk}");
            ok = false;
        }

        // And it has no ability, because an ability on the default is an ability
        // every existing balance number was measured with.
        if (first.StartingBlades != 0 || first.StartingChill > 0.0f
            || Mathf.Abs(first.LootValueScale - 1.0f) > 0.001f
            || first.SearchRadiusBonus > 0.0f)
        {
            GD.PushError($"  {first.CharacterName} carries an ability into a game tuned without one");
            ok = false;
        }

        if (first.OpensAfter != 0)
        {
            GD.PushError($"  {first.CharacterName} is the starting survivor and cannot be locked");
            ok = false;
        }

        var names = new System.Collections.Generic.List<string>();
        foreach (CharacterResource one in all)
            names.Add(one.CharacterName);

        GD.Print($"  {string.Join(", ", names)}");
        return ok;
    }

    /// Every survivor gives something up.
    ///
    /// Compared against the Drifter rather than against each other, because the
    /// Drifter is the zero: it is what "no trade" looks like, and a survivor that
    /// beats it on every axis is a strictly better version of the game's baseline
    /// with a different name on it.
    private bool StageNoStrictlyBetter()
    {
        bool ok = true;

        foreach (CharacterResource one in CharacterBook.All)
        {
            if (one.OpensAfter == 0)
                continue;

            bool health = one.MaxHealth > DrifterHealth + 0.01f;
            bool speed = one.MoveSpeed > DrifterSpeed + 0.01f;
            bool bulk = one.CarryCapacity > DrifterBulk;

            // Gives something up on at least one of the three the player feels.
            // The abilities are deliberately not counted here: they are all
            // upside, and a survivor could otherwise "pay" for a bigger bag with
            // a smaller bonus, which is not a cost the player experiences.
            bool pays = one.MaxHealth < DrifterHealth - 0.01f
                     || one.MoveSpeed < DrifterSpeed - 0.01f
                     || one.CarryCapacity < DrifterBulk;

            GD.Print($"  {one.CharacterName,-8} {one.MaxHealth:F0} hp {one.MoveSpeed:F1} m/s "
                   + $"{one.CarryCapacity} bulk — "
                   + (pays ? "pays for it" : "PAYS NOTHING"));

            if (!pays)
            {
                GD.PushError($"  {one.CharacterName} is better than the Drifter at everything "
                           + "it changes — that is a difficulty setting with a name");
                ok = false;
            }

            // And it has to be better at *something*, or it is a handicap.
            if (!health && !speed && !bulk
                && one.StartingBlades == 0 && one.StartingChill <= 0.0f
                && Mathf.Abs(one.LootValueScale - 1.0f) < 0.001f
                && one.SearchRadiusBonus <= 0.0f)
            {
                GD.PushError($"  {one.CharacterName} gives up something and gains nothing");
                ok = false;
            }
        }

        return ok;
    }

    /// Big enough to change how the run is played.
    ///
    /// A survivor with ninety-eight health instead of a hundred is a rounding
    /// error the player will never notice, and a roster of those is a menu that
    /// exists to look like a feature. Fifteen per cent is roughly where a change
    /// to any of these three stops being deniable — it is one extra mistake, or
    /// one fewer crate, or half a second across a street.
    private bool StageDifferencesAreReal()
    {
        const float Enough = 0.15f;
        bool ok = true;

        foreach (CharacterResource one in CharacterBook.All)
        {
            if (one.OpensAfter == 0)
                continue;

            float health = Mathf.Abs(one.MaxHealth - DrifterHealth) / DrifterHealth;
            float speed = Mathf.Abs(one.MoveSpeed - DrifterSpeed) / DrifterSpeed;
            float bulk = Mathf.Abs(one.CarryCapacity - DrifterBulk) / (float)DrifterBulk;
            float most = Mathf.Max(health, Mathf.Max(speed, bulk));

            GD.Print($"  {one.CharacterName,-8} differs by {health * 100.0f:F0}% hp, "
                   + $"{speed * 100.0f:F0}% speed, {bulk * 100.0f:F0}% bulk");

            if (most < Enough)
            {
                GD.PushError($"  {one.CharacterName}'s biggest difference is "
                           + $"{most * 100.0f:F0}% — nobody will feel that");
                ok = false;
            }
        }

        return ok;
    }

    /// An ability is an existing `RunModifiers` field, granted early.
    ///
    /// That is the design and it is worth a stage, because the tempting thing to
    /// do with a character is give it a mechanic of its own — and a mechanic only
    /// one survivor has is a mechanic the deck, the gear and the trinkets cannot
    /// interact with. Every ability here reaches a number the rest of the game
    /// already reaches, so a survivor is a *head start on a strategy* the player
    /// can then build around.
    ///
    /// Asserted by applying them to a fresh `RunModifiers` and checking they land,
    /// which is the same call `Player.ApplyCharacter` makes.
    private bool StageAbilitiesAreModifiers()
    {
        bool ok = true;
        int withAbilities = 0;

        foreach (CharacterResource one in CharacterBook.All)
        {
            var mods = new RunModifiers();

            mods.OrbitBlades += one.StartingBlades;
            mods.Chill = Mathf.Max(mods.Chill, one.StartingChill);
            mods.LootValueScale *= one.LootValueScale;
            mods.SearchRadiusBonus += one.SearchRadiusBonus;

            bool any = mods.OrbitBlades > 0 || mods.Chill > 0.0f
                    || Mathf.Abs(mods.LootValueScale - 1.0f) > 0.001f
                    || mods.SearchRadiusBonus > 0.0f;

            if (any)
                withAbilities++;

            GD.Print($"  {one.CharacterName,-8} blades {mods.OrbitBlades}, chill {mods.Chill:F2}, "
                   + $"loot x{mods.LootValueScale:F2}, reach +{mods.SearchRadiusBonus:F1} m");
        }

        // The premise. With no abilities anywhere this stage passes on a system
        // that does nothing, and would go on passing after somebody deleted it.
        if (withAbilities == 0)
        {
            GD.PushError("  no survivor has an ability — nothing here was exercised");
            ok = false;
        }

        return ok;
    }

    /// The roster opens in order, and the order costs something.
    private bool StageUnlockOrder()
    {
        bool ok = true;
        int previous = -1;

        foreach (CharacterResource one in CharacterBook.All)
        {
            if (one.OpensAfter <= previous)
            {
                GD.PushError($"  {one.CharacterName} opens after {one.OpensAfter} extractions, "
                           + $"which is not later than the {previous} before it");
                ok = false;
            }

            previous = one.OpensAfter;
        }

        // And a fresh profile can only be the first one, or the gate is not a
        // gate.
        var fresh = new Profile();
        int allowed = 0;
        for (int i = 0; i < CharacterBook.All.Length; i++)
        {
            if (CharacterBook.Allows(fresh, i))
                allowed++;
        }

        GD.Print($"  a new profile may play {allowed} of {CharacterBook.All.Length}");

        if (allowed != 1)
        {
            GD.PushError($"  a new profile can already play {allowed} survivors");
            ok = false;
        }

        return ok;
    }
}
