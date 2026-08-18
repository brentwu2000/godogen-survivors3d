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
        }

        _player?.ApplyGear(health, armour, speed, carry, safeBox);
        _growth?.SetCaps(healthCap, armourCap, speedCap, searchCap);
    }

    /// Called before starting a run from the base screen.
    public void SetLoadout(string weaponPath)
    {
        Profile.LoadoutWeapon = weaponPath;
        Persist();
    }

    private void OnRunEnded(int state, int bankedValue)
    {
        var runState = (RunState)state;
        bool survived = runState == RunState.Extracted;

        // Stash contents follow the same rule as credits: everything on an
        // extraction, only the safe box otherwise.
        if (_player != null)
        {
            if (survived)
                StashAll(_player.Backpack);

            StashAll(_player.SafeBox);
        }

        Profile.Credits += bankedValue;

        string[] lost = System.Array.Empty<string>();
        if (survived)
        {
            Profile.RunsSurvived++;
        }
        else
        {
            Profile.RunsLost++;
            lost = LoseCarriedEquipment();
        }

        // Practice is knowledge, not cargo — it survives a death. Banked once
        // here rather than levelled as it is earned, so it stays a separate and
        // much slower curve from the growth inside the run.
        var practice = new int[Profile.Proficiency.Length];
        if (_weapons != null)
        {
            for (int i = 0; i < Profile.Proficiency.Length; i++)
            {
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
        LastRun = _log?.Freeze(runState, bankedValue, practice, lost)
                  ?? new RunRecord { Outcome = runState, Banked = bankedValue };

        LastRecordsBeaten = Profile.ApplyRecords(LastRun);
        SettleContract(LastRun);

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

    private void StashAll(Inventory inventory)
    {
        for (int i = 0; i < inventory.EntryCount; i++)
            Profile.AddToStash(inventory.ItemAt(i).ItemName, inventory.CountAt(i));
    }

    private void Persist()
    {
        if (!Ephemeral)
            SaveSystem.Save(Profile);
    }
}
