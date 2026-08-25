using Godot;

/// The layer above a single run: loads the profile, applies the loadout, and
/// banks whatever survived when the run ends.
///
/// It owns the decision about what carries over, rather than the run director,
/// so the rule "the backpack is lost, the safe box is not" lives in one place.
public partial class MetaManager : Node
{
    /// Skips disk entirely. Probes set this so a test run cannot overwrite a
    /// real profile.
    [Export] public bool Ephemeral { get; set; }

    [Signal] public delegate void ProfileBankedEventHandler(int creditsGained, int totalCredits);

    public Profile Profile { get; private set; } = new();

    /// The finished run, once there is one. The debrief reads it; so does anything
    /// that wants to know how the last run went without re-deriving it.
    public RunRecord? LastRun { get; private set; }

    public Profile.RecordsBeaten LastRecordsBeaten { get; private set; }

    /// What the last run opened. Held rather than only printed, because the
    /// moment an unlock matters is the moment the player is deciding what to do
    /// next — and that is the debrief and the base screen, not the log.
    public System.Collections.Generic.List<Unlock> NewUnlocks { get; private set; } = new();

    /// Whether the contract taken into this run was met, and what it paid.
    public bool ContractMet { get; private set; }
    public Contract? ContractTaken { get; private set; }

    private RunDirector? _director;
    private Player? _player;
    private WeaponHandler? _weapons;
    private RunGrowth? _growth;
    private RunLog? _log;

    public override void _Ready()
    {
        Profile = Ephemeral ? new Profile() : SaveSystem.Load();

        _director = GetParent().GetNodeOrNull<RunDirector>("RunDirector");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
        _growth = GetParent().GetNodeOrNull<RunGrowth>("RunGrowth");
        _log = GetParent().GetNodeOrNull<RunLog>("RunLog");

        ApplyLoadout();

        if (_director != null)
            _director.RunEnded += OnRunEnded;
    }

    private void ApplyLoadout()
    {
        ApplyGear();

        if (_weapons == null)
            return;

        for (int i = 0; i < Profile.Proficiency.Length; i++)
            _weapons.SetProficiency((WeaponCategory)i, Profile.Proficiency[i]);

        // Slot 1 last would leave it active; the primary is what a run starts
        // holding, and the sidearm is what it falls back to.
        EquipInto(1, Profile.LoadoutSecondary);
        EquipInto(0, Profile.LoadoutWeapon);
    }

    /// Tilts the deck toward what a carried weapon is for.
    private void FavourFrom(string path, float strength)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var weapon = GD.Load<WeaponResource>(path);
        if (weapon != null)
            _growth?.FavourLine(weapon.Favours, weapon.FavourStrength * strength);
    }

    private void EquipInto(int slot, string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var weapon = GD.Load<WeaponResource>(path);
        if (weapon != null)
            _weapons!.Equip(slot, weapon);
        else
            GD.PushWarning($"MetaManager: loadout {path} did not load; slot {slot} left empty");
    }

    /// Sums the equipped pieces into the player's starting stats and into how
    /// many upgrades the run may take of each kind. Both halves come from the
    /// same rows, so a piece cannot raise a ceiling it does not also justify.
    private void ApplyGear()
    {
        float health = 0.0f, armour = 0.0f, speed = 0.0f;
        int carry = 0, safeBox = 0;
        int healthCap = 0, armourCap = 0, speedCap = 0, searchCap = 0;

        // Plain locals rather than a RunModifiers to accumulate into. That was
        // the first shape and it is a trap: RunModifiers.AreaScale is neutral at
        // 1, so summing three pieces that each grant nothing into one would have
        // produced a triple-size blast for a player wearing the starting kit.
        // Each of these is neutral at zero and means "what the gear adds".
        int pierce = 0;
        float area = 0.0f, thorns = 0.0f, regen = 0.0f, knockback = 0.0f, dodge = 0.0f;
        int orbit = 0, shockwave = 0;
        float chain = 0.0f, chill = 0.0f;

        // Cleared before the loop, not after: `ReapplyGearForTesting` calls this
        // twice inside one process, and a lean that accumulated would make the
        // second reading of the same loadout twice as committed as the first.
        _growth?.ClearFavour();

        // The weapons lean it too, and in the same pass as the gear because
        // `ClearFavour` runs at the top of this one — applied anywhere else, a
        // re-equip in the base screen would wipe the loadout's half of the lean
        // and leave the deck tilted by the armour alone.
        //
        // **Primary at full strength, Sidearm at half.** The sidearm is the
        // smaller half of a pair and should move the deck by less than the thing
        // filling both hands; equal weights would make a knife worth as much of
        // a commitment as the rifle it is covering for.
        FavourFrom(Profile.LoadoutWeapon, 1.0f);
        FavourFrom(Profile.LoadoutSecondary, 0.5f);

        var ruleCaps = new System.Collections.Generic.Dictionary<GrowthOption, int>();

        foreach (string path in Profile.EquippedGear)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            // Only what the player actually owns. A piece lost on the last run
            // is still named in the slot until the base screen replaces it, and
            // wearing it anyway would make death cost nothing.
            if (!Profile.Owns(path))
                continue;

            var piece = GD.Load<GearResource>(path);
            if (piece == null)
            {
                GD.PushWarning($"MetaManager: gear {path} did not load — run BuildGear.cs");
                continue;
            }

            health += piece.HealthBonus;
            armour += piece.ArmourBonus;
            speed += piece.MoveSpeedBonus;
            carry += piece.CarryBonus;
            safeBox += piece.SafeBoxBonus;

            healthCap += piece.HealthUpgradeCap;
            armourCap += piece.ArmourUpgradeCap;
            speedCap += piece.SpeedUpgradeCap;
            searchCap += piece.SearchUpgradeCap;

            pierce += piece.PierceBonus;
            area += piece.AreaBonus;
            thorns += piece.ThornsBonus;
            regen += piece.RegenBonus;
            knockback += piece.KnockbackBonus;
            dodge += piece.DodgeBonus;

            // What this piece makes the run become, as distinct from what it adds
            // to it. See `GearResource.Favours`.
            _growth?.FavourLine(piece.Favours, piece.FavourStrength);

            orbit += piece.OrbitBonus;
            shockwave += piece.ShockwaveBonus;
            chain += piece.ChainBonus;

            // Compounded, not summed, so two chill sources land on the same curve
            // the growth card uses. Summed, a trinket and two picks could exceed
            // one between them and stop the horde dead.
            if (piece.ChillBonus > 0.0f)
                chill = 1.0f - (1.0f - chill) * (1.0f - piece.ChillBonus);

            // Summed like every other cap, but only where a piece has an opinion.
            // -1 is "no opinion", which is not the same as zero: three pieces
            // that each say nothing about pierce must leave the default alone,
            // and summing zeroes would silently forbid it.
            Opinion(ruleCaps, GrowthOption.Pierce, piece.PierceUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Crit, piece.CritUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Area, piece.AreaUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Thorns, piece.ThornsUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Regen, piece.RegenUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Knockback, piece.KnockbackUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Dodge, piece.DodgeUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Fortune, piece.FortuneUpgradeCap);

            Opinion(ruleCaps, GrowthOption.Orbit, piece.OrbitUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Shockwave, piece.ShockwaveUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Chain, piece.ChainUpgradeCap);
            Opinion(ruleCaps, GrowthOption.Chill, piece.ChillUpgradeCap);
        }

        _player?.ApplyGear(health, armour, speed, carry, safeBox);
        _player?.ApplyGearRules(pierce, area, thorns, regen, knockback, dodge);
        _player?.ApplyGearKit(orbit, shockwave, chain, chill);
        _growth?.SetCaps(healthCap, armourCap, speedCap, searchCap, ruleCaps);
    }

    /// Re-reads the equipped set. For probes comparing two loadouts inside one
    /// scene, which is the only way to hold the seed still while the gear
    /// changes — two processes would differ in the level layout as well.
    ///
    /// The stat half compounds when called twice, because `Player.ApplyGear` adds
    /// rather than assigns. That is correct for its real caller, which runs once,
    /// and it means a probe using this must measure rules and ceilings rather
    /// than health.
    public void ReapplyGearForTesting() => ApplyGear();

    private static void Opinion(System.Collections.Generic.Dictionary<GrowthOption, int> caps,
                                GrowthOption option, int value)
    {
        if (value < 0)
            return;

        caps[option] = caps.TryGetValue(option, out int existing) ? existing + value : value;
    }

    /// Called before starting a run from the base screen.
    public void SetLoadout(string weaponPath)
    {
        Profile.LoadoutWeapon = weaponPath;
        Persist();
    }

    private void OnRunEnded(int state, int bankedValue)
    {
        // Cleared here, at the top of a settle, rather than left to the object's
        // lifetime.
        //
        // In practice one manager sees one run — the scene is torn down after the
        // debrief — so these start empty and the clearing is redundant today. It
        // is here because the comment on the fields said they were cleared when a
        // run began and nothing cleared them, which is a comment that would have
        // been believed. A second settle on one manager would have reported the
        // first run's finds again, and "you recovered a wedding ring" appearing
        // twice for one ring is the kind of wrong that gets read as a save bug.
        _foundThisRun.Clear();
        _bountyThisRun = 0;

        var runState = (RunState)state;
        bool survived = runState == RunState.Extracted;

        // A daily settles nothing into the profile — no credits, no stash, no
        // equipment lost, and no run counted. It is a scored attempt at a fixed
        // puzzle, and the moment it also pays better than an ordinary run, the
        // ordinary run becomes the practice mode.
        //
        // The symmetry matters as much as the rule. A daily that cost gear but
        // paid nothing would be a mode nobody takes their good rifle into, which
        // is a different way of not being played.
        //
        // Guarded rather than returned early: the record still has to be frozen,
        // the score still has to be written, and the debrief still has to run.
        // The first version of this returned here and switched the whole mode off
        // — including the part that records the result.
        bool settles = !GameSession.IsDaily;

        // Stash contents follow the same rule as credits: everything on an
        // extraction, only the safe box otherwise.
        if (settles && _player != null)
        {
            if (survived)
                StashAll(_player.Backpack);

            StashAll(_player.SafeBox);
        }

        string[] lost = System.Array.Empty<string>();

        if (settles)
        {
            Profile.Credits += bankedValue;

            if (survived)
            {
                Profile.RunsSurvived++;
            }
            else
            {
                Profile.RunsLost++;
                lost = LoseCarriedEquipment();
            }
        }

        // Practice is knowledge, not cargo — it survives a death. Banked once
        // here rather than levelled as it is earned, so it stays a separate and
        // much slower curve from the growth inside the run.
        var practice = new int[Profile.Proficiency.Length];
        var hits = new int[Profile.Proficiency.Length];
        if (_weapons != null)
        {
            for (int i = 0; i < Profile.Proficiency.Length; i++)
            {
                hits[i] = _weapons.HitsThisRun((WeaponCategory)i);

                int gained = _weapons.ProficiencyGain((WeaponCategory)i);
                if (gained <= 0)
                    continue;

                practice[i] = gained;
                Profile.Proficiency[i] += gained;
            }
        }

        // The record is assembled before the contract is judged and before the
        // records are folded in, because both of those read it. One set of
        // numbers, three consumers — a contract that counted kills its own way
        // would disagree with the screen reporting them, and the player would be
        // right to trust neither.
        string[] curiosities = _foundThisRun.ToArray();

        LastRun = _log?.Freeze(runState, bankedValue, practice, hits, lost, curiosities, _bountyThisRun)
                  ?? new RunRecord
                  {
                      Outcome = runState,
                      Banked = bankedValue,
                      CuriositiesFound = curiosities,
                      SetBounty = _bountyThisRun,
                  };

        // Records and unlocks are part of settling too. A daily that could set a
        // personal best would let a favourable fixed seed stand in for a run the
        // player actually had, and a daily death that broke the extraction streak
        // would make the mode something to avoid on a good streak — which is the
        // opposite of a reason to come back.
        LastRecordsBeaten = settles ? Profile.ApplyRecords(LastRun) : default;
        SettleContract(LastRun);

        // After ApplyRecords, not before. Two conditions ask about a career
        // rather than an evening — a streak of three is only three once this run
        // has been counted — and asking first would delay those unlocks by
        // exactly one run, which looks from the outside like a condition that
        // does not work.
        NewUnlocks = new System.Collections.Generic.List<Unlock>();

        if (settles)
        {
            Profile.BossesKilled += LastRun.BossesKilled;
            NewUnlocks = UnlockBook.NewlyMet(LastRun, Profile);
            foreach (Unlock unlock in NewUnlocks)
            {
                Profile.Open(unlock.Id);
                GD.Print($"unlocked: {unlock.Name} ({unlock.Condition})");
            }
        }

        Persist();
        GD.Print($"profile: credits {Profile.Credits} (+{bankedValue}), " +
                 $"survived {Profile.RunsSurvived} lost {Profile.RunsLost}, streak {Profile.Streak}");
        EmitSignal(SignalName.ProfileBanked, bankedValue, Profile.Credits);

        // Only for a run that came from the base screen: a probe owns its tree,
        // and swapping the scene underneath one mid-measurement would end the
        // test rather than the run.
        if (GameSession.LaunchedFromBase)
            ShowDebrief();
    }

    /// Pays the job if the run satisfied it, then puts three new ones up.
    ///
    /// The board is re-rolled either way. A failed contract that stayed on offer
    /// would let a player retry the same easy card until it landed, which turns a
    /// commitment made before leaving into a formality.
    private void SettleContract(RunRecord run)
    {
        // A daily carries its own job and leaves the player's board alone.
        //
        // Overwriting `ContractIndex` to run the daily would spend whatever the
        // player had committed to before they left — the board is a decision made
        // in advance, and a side mode that silently cashes it in is a side mode
        // that costs something it never said it would.
        if (GameSession.IsDaily)
        {
            ContractTaken = GameSession.DailyJob;
            ContractMet = GameSession.DailyJob.IsMet(run);

            int score = DailyRun.Score(run, ContractMet);
            Profile.RecordDaily(GameSession.DailyKey, score);

            GD.Print($"daily {GameSession.DailyKey}: {score} " +
                     $"({GameSession.DailyJob.Describe(_log)} — {(ContractMet ? "met" : "missed")}), " +
                     $"streak {Profile.DailyStreak(GameSession.DailyKey)}");
            return;
        }

        ContractTaken = Profile.AcceptedContract;
        ContractMet = ContractTaken?.IsMet(run) ?? false;

        if (ContractTaken is { } contract && ContractMet)
        {
            Profile.Credits += contract.Reward;
            GD.Print($"contract met: {contract.Describe(_log)} (+{contract.Reward})");
        }

        Profile.RollContracts();
    }

    /// Hands the run back to the player before handing them back to the shop.
    ///
    /// This used to be a three and a half second timer, which was long enough to
    /// not finish reading a two-line banner and far too short for everything the
    /// run actually produced. The screen waits for a key instead — the payoff is
    /// the one moment in the loop that should not be on a clock.
    private void ShowDebrief()
    {
        GameSession.LaunchedFromBase = false;

        var debrief = GetParent().GetNodeOrNull<DebriefScreen>("Debrief");
        if (debrief != null)
        {
            debrief.Show(this, _log);
            return;
        }

        // No screen in this scene: go straight back rather than stranding the
        // player in a finished run with no way out.
        GD.PushWarning("MetaManager: no Debrief node — returning to base directly");
        if (IsInsideTree())
            GetTree().ChangeSceneToFile("res://scenes/Base.tscn");
    }

    /// Dying leaves the good kit on the ground. Starting kit is exempt — it is
    /// the shirt on their back, and a player who cannot afford a backpack has to
    /// still have one or the loop has no next run.
    ///
    /// This is the rule that makes the shop a decision rather than a one-time
    /// unlock: buying the better rifle is easy, taking it out is the wager.
    private string[] LoseCarriedEquipment()
    {
        var lost = new System.Collections.Generic.List<string>();

        foreach (string path in Profile.EquippedGear)
        {
            if (!string.IsNullOrEmpty(path) && Profile.Revoke(path))
                lost.Add(path);
        }

        if (Profile.Revoke(Profile.LoadoutWeapon))
            lost.Add(Profile.LoadoutWeapon);

        if (Profile.Revoke(Profile.LoadoutSecondary))
            lost.Add(Profile.LoadoutSecondary);

        if (lost.Count > 0)
            GD.Print($"lost on death: {string.Join(", ", lost)}");

        return lost.ToArray();
    }

    /// Everything carried out goes into the stash, and curiosities are noted on
    /// the way past.
    ///
    /// Recorded here rather than at the locker, so selling the stash for credits
    /// — which is the ordinary thing to do with it — does not forfeit a set the
    /// player has already earned.
    private void StashAll(Inventory inventory)
    {
        for (int i = 0; i < inventory.EntryCount; i++)
        {
            string name = inventory.ItemAt(i).ItemName;

            // Noted before `Record`, which is what makes it "for the first time":
            // afterwards the profile already knows and the run cannot say what it
            // contributed.
            if (CollectionBook.SetOf(name) >= 0 && !Profile.Collected.Contains(name))
                _foundThisRun.Add(name);

            Profile.AddToStash(name, inventory.CountAt(i));
            Profile.Record(name);
        }

        _bountyThisRun += CollectionBook.Claim(Profile);
    }

    /// What this run added to the collection. Cleared at the top of `OnRunEnded`,
    /// so the answer belongs to the settle that is reporting it rather than to
    /// however long this node happens to live.
    private readonly System.Collections.Generic.List<string> _foundThisRun = new();
    private int _bountyThisRun;

    private void Persist()
    {
        if (!Ephemeral)
            SaveSystem.Save(Profile);
    }
}
