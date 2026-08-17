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

    private RunDirector? _director;
    private Player? _player;
    private WeaponHandler? _weapons;

    public override void _Ready()
    {
        Profile = Ephemeral ? new Profile() : SaveSystem.Load();

        _director = GetParent().GetNodeOrNull<RunDirector>("RunDirector");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        ApplyLoadout();

        if (_director != null)
            _director.RunEnded += OnRunEnded;
    }

    private void ApplyLoadout()
    {
        if (_weapons == null)
            return;

        for (int i = 0; i < Profile.Proficiency.Length; i++)
            _weapons.SetProficiency((WeaponCategory)i, Profile.Proficiency[i]);

        var weapon = GD.Load<WeaponResource>(Profile.LoadoutWeapon);
        if (weapon != null)
            _weapons.Equip(weapon);
        else
            GD.PushWarning($"MetaManager: loadout {Profile.LoadoutWeapon} did not load; keeping the default");
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
            Profile.RunsSurvived++;
        else
            Profile.RunsLost++;

        // Practice is knowledge, not cargo — it survives a death.
        if (_weapons != null)
        {
            for (int i = 0; i < Profile.Proficiency.Length; i++)
                Profile.Proficiency[i] = _weapons.GetProficiency((WeaponCategory)i);
        }

        Persist();
        GD.Print($"profile: credits {Profile.Credits} (+{bankedValue}), " +
                 $"survived {Profile.RunsSurvived} lost {Profile.RunsLost}");
        EmitSignal(SignalName.ProfileBanked, bankedValue, Profile.Credits);
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
