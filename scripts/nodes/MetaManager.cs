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

    /// Where the run returns when it ends. Only a run that came from the base
    /// screen goes back to it — probes and capture scripts own their own tree.
    [Export] public float ReturnDelaySeconds { get; set; } = 3.5f;

    private RunDirector? _director;
    private Player? _player;
    private WeaponHandler? _weapons;
    private RunGrowth? _growth;

    public override void _Ready()
    {
        Profile = Ephemeral ? new Profile() : SaveSystem.Load();

        _director = GetParent().GetNodeOrNull<RunDirector>("RunDirector");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
        _growth = GetParent().GetNodeOrNull<RunGrowth>("RunGrowth");

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

        if (survived)
        {
            Profile.RunsSurvived++;
        }
        else
        {
            Profile.RunsLost++;
            LoseCarriedEquipment();
        }

        // Practice is knowledge, not cargo — it survives a death. Banked once
        // here rather than levelled as it is earned, so it stays a separate and
        // much slower curve from the growth inside the run.
        if (_weapons != null)
        {
            for (int i = 0; i < Profile.Proficiency.Length; i++)
            {
                int gained = _weapons.ProficiencyGain((WeaponCategory)i);
                if (gained <= 0)
                    continue;

                Profile.Proficiency[i] += gained;
                GD.Print($"practice: {(WeaponCategory)i} +{gained} " +
                         $"(now {Profile.Proficiency[i]}, {_weapons.HitsThisRun((WeaponCategory)i)} hits)");
            }
        }

        Persist();
        GD.Print($"profile: credits {Profile.Credits} (+{bankedValue}), " +
                 $"survived {Profile.RunsSurvived} lost {Profile.RunsLost}");
        EmitSignal(SignalName.ProfileBanked, bankedValue, Profile.Credits);

        // Long enough to read the banner. Only for a run that came from the base
        // screen: a probe owns its tree, and swapping the scene underneath one
        // mid-measurement would end the test rather than the run.
        if (GameSession.LaunchedFromBase)
            ReturnToBase();
    }

    /// Dying leaves the good kit on the ground. Starting kit is exempt — it is
    /// the shirt on their back, and a player who cannot afford a backpack has to
    /// still have one or the loop has no next run.
    ///
    /// This is the rule that makes the shop a decision rather than a one-time
    /// unlock: buying the better rifle is easy, taking it out is the wager.
    private void LoseCarriedEquipment()
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
    }

    private void StashAll(Inventory inventory)
    {
        for (int i = 0; i < inventory.EntryCount; i++)
            Profile.AddToStash(inventory.ItemAt(i).ItemName, inventory.CountAt(i));
    }

    private async void ReturnToBase()
    {
        GameSession.LaunchedFromBase = false;
        await ToSignal(GetTree().CreateTimer(ReturnDelaySeconds), SceneTreeTimer.SignalName.Timeout);

        // The tree can be gone by the time the timer fires if the window was
        // closed during the wait.
        if (IsInsideTree())
            GetTree().ChangeSceneToFile("res://scenes/Base.tscn");
    }

    private void Persist()
    {
        if (!Ephemeral)
            SaveSystem.Save(Profile);
    }
}
