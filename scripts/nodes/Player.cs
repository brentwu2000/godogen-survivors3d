using Godot;

public partial class Player : CharacterBody3D
{
    [Export] public float MoveSpeed { get; set; } = 6.0f;

    /// Whether the horizontal stick turns the view instead of strafing.
    ///
    /// True is the game. False is the eight-way scheme this project had for
    /// twenty-nine phases, kept working rather than deleted — it is the only way
    /// to answer "is this the controls or is this the thing I just changed" when
    /// a movement bug shows up, and a scheme that has rotted in a branch nobody
    /// runs cannot answer anything.
    [Export] public bool TurnToSteer { get; set; } = true;

    /// A solid body instead of a billboard sprite.
    ///
    /// True is the game. The sprite path stays working for the same reason
    /// `Horde.SolidBodies` keeps its own — and because a player left as a
    /// billboard among solid enemies is the fastest way to see whether a problem
    /// is in the body pipeline or somewhere else.
    [Export] public bool SolidBody { get; set; } = true;

    /// How tall the player draws. Slightly over the walker's two metres, because
    /// the one body that must never be mistaken for the horde is this one.
    [Export] public float BodyHeight { get; set; } = 2.2f;

    /// Degrees per second `[A]`/`[D]` turn the view. Read off the rig so the
    /// movement keys and the view keys cannot end up at different rates.
    private float TurnRateDegrees => _rig?.TurnRateDegrees ?? 150.0f;

    /// Higher settles onto the target velocity faster. This is a rate, not a
    /// per-tick fraction — see the damping note in _PhysicsProcess.
    [Export] public float AccelerationRate { get; set; } = 14.0f;

    [Export] public float MaxHealth { get; set; } = 100.0f;

    /// Backpack slots. Small enough that a full run forces a choice about what to
    /// carry out.
    [Export] public int CarryCapacity { get; set; } = 20;

    [Signal] public delegate void DiedEventHandler();

    /// Something left the backpack and did something. Two events rather than one
    /// because spending a medkit and throwing a pipe bomb are different answers
    /// to different questions, and a contract asking "did you get out without
    /// healing" must not be satisfied by never having thrown anything.
    ///
    /// Carries the item name rather than the resource: the only readers are a log
    /// and a readout, and a name is what both of them want.
    public event System.Action<string>? ItemUsed;

    /// The item's name and how many the throw killed outright. The count rides
    /// along because the alternative is the log counting deaths in a window after
    /// a throw, which would credit the grenade with whatever the rifle did during
    /// the same second.
    public event System.Action<string, int>? ItemThrown;

    /// Bulk the safe box holds. Deliberately tiny: it is a hedge against a bad
    /// death, not a second backpack.
    [Export] public int SafeBoxCapacity { get; set; } = 4;

    /// Flat mitigation, subtracted from an incoming rate or amount. Never scales
    /// it: armour is the answer to a crowd of weak contacts and never the answer
    /// to a brute, and that asymmetry is what makes it worth picking over damage.
    public float Armour { get; private set; }

    /// Multiplies how fast a container fills. One place so the loot container
    /// does not have to know what a player upgrade is.
    public float SearchSpeed { get; private set; } = 1.0f;

    /// What this run's upgrades have changed, for everything that reads a rule
    /// rather than a number. Lives on the player because a run is a player;
    /// read by the weapon, the horde and the loot containers at the point of use.
    public RunModifiers Mods { get; } = new();

    public float Health { get; private set; }
    public bool IsAlive => Health > 0.0f;
    public Inventory Backpack { get; private set; } = null!;

    /// Survives death. Everything in the backpack does not.
    public Inventory SafeBox { get; private set; } = null!;

    /// Last non-zero heading, in world XZ. Weapons fall back to this when there
    /// is nothing to aim at, so a swing at empty air still goes somewhere sensible.
    public Vector2 Facing { get; private set; } = Vector2.Down;

    // Both are assigned in _Ready, which always runs before anything reads them.
    private IInputSource _input = null!;
    private Sprite3D _sprite = null!;
    private CameraRig? _rig;
    private SoloBody? _body;
    private WeaponHandler? _weapons;

    /// The silhouette the body was last built with, and the shader to rebuild
    /// it with.
    ///
    /// The player carried nothing on screen until now, and the reason it is a
    /// rebuild rather than a toggle is the rig: a `MultiMesh` has no skeleton,
    /// so a held object is geometry *inside* the body mesh turning on the arm's
    /// pivot. Changing what is held changes the mesh.
    ///
    /// That is cheap at the rate it happens. Swapping weapons is a keypress the
    /// player makes a handful of times a run, and the body is about fifteen
    /// hundred triangles — an order of magnitude less than one frame of the
    /// horde. Rebuilding on the *category* rather than on the weapon means a
    /// rifle traded for another rifle costs nothing at all.
    private BodyMeshLibrary.Carry _held = BodyMeshLibrary.Carry.None;
    private Shader? _bodyShader;

    /// Rebuilds the body if what it is holding has changed.
    ///
    /// Called every frame and almost always does nothing, which is the point:
    /// there is no event for "the weapon changed" that covers every path into
    /// it — a swap, a shop purchase carried into a run, a pickup, and the first
    /// frame before any weapon has resolved at all. Comparing the answer is
    /// cheaper than finding every place that could change it, and it cannot go
    /// stale the way a subscription can.
    private void CarryChanged()
    {
        if (_body == null || _bodyShader == null)
            return;

        BodyMeshLibrary.Carry want = _weapons?.Weapon is WeaponResource held
            ? BodyMeshLibrary.CarryFor(held.Category)
            : BodyMeshLibrary.Carry.None;

        if (want == _held)
            return;

        // Not yet in the tree, so there is nothing to swap into.
        //
        // The first body is added with `CallDeferred` — the parent is still
        // setting up its children when `_Ready` runs and a direct `AddChild` is
        // refused — so for the first frames its node has no parent. Taking the
        // parent as null and carrying on adds the replacement to nothing, and
        // the player is simply not drawn: no error, no warning, an empty patch
        // of floor where the survivor should be.
        //
        // **Before `_held` is written, not after.** Recording the change and
        // then bailing means the comparison above never fires again, so the body
        // stays whatever it was built as — empty-handed, permanently, which is
        // the bug this whole function exists to fix. Returning here retries next
        // frame, by which point the deferred insertion has happened.
        Node? parent = _body.Node.GetParent();
        if (parent == null)
            return;

        _held = want;

        // The old body's node leaves the tree before the new one enters it.
        // Two SoloBodies drawing the same player is one player wearing two
        // weapons, and the second is only invisible because they overlap.
        parent.RemoveChild(_body.Node);
        _body.Node.QueueFree();

        _body = new SoloBody(_bodyShader, BodySpec(), _horde?.ArenaExtent ?? 60.0f);

        parent.AddChild(_body.Node);
    }
    private Horde? _horde;

    /// Lets the HUD install the touch source once the sticks exist. Without a
    /// call, the player falls back to keyboard and mouse.
    public void SetInputSource(IInputSource source) => _input = source;

    /// Which implementation is installed. Only a probe asks — and it has to,
    /// because "the touch layer is present" and "the touch layer is connected"
    /// looked identical for sixteen phases, and the second one was false.
    public string InputSourceName => _input?.GetType().Name ?? "none";

    public override void _Ready()
    {
        _sprite = GetNode<Sprite3D>("Sprite");
        _weapons = GetNodeOrNull<WeaponHandler>("WeaponHandler");
        _horde = GetParent()?.GetNodeOrNull<Horde>("Horde");
        ApplyCharacter();

        _rig = GetParent()?.GetNodeOrNull<CameraRig>("CameraRig");
        _input ??= new KeyboardMouseInput(GetViewport().GetCamera3D());

        if (SolidBody)
        {
            var bodyShader = GD.Load<Shader>("res://assets/shaders/body.gdshader");
            if (bodyShader == null)
            {
                // Falling back rather than failing, for the same reason the horde
                // does: a missing shader is a build problem, and an invisible
                // player is much harder to diagnose than a sprite with a warning.
                GD.PushWarning("Player: body.gdshader missing — staying a sprite");
                SolidBody = false;
            }
            else
            {
                _bodyShader = bodyShader;
                _body = new SoloBody(bodyShader, BodySpec(), _horde?.ArenaExtent ?? 60.0f);

                // Added to the parent, not to the player. The transform inside a
                // MultiMesh is world space, so a node parented to a moving player
                // would apply the movement twice — the body would run away at
                // double speed, which looks like a movement bug rather than a
                // parenting one.
                //
                // Deferred, because the parent is still setting up its children
                // while this _Ready runs and a direct AddChild is refused:
                //
                //   Parent node is busy setting up children, `add_child()` failed.
                //
                // Which is printed, and is not an exception. The player carries on
                // with a body that exists, updates every frame, and is not in the
                // tree — so it draws nothing at all, and the C# holding it looks
                // entirely correct.
                GetParent()?.CallDeferred(Node.MethodName.AddChild, _body.Node);
            }

            _sprite.Visible = !SolidBody;
        }
        Health = MaxHealth;
        Backpack = new Inventory(CarryCapacity);
        SafeBox = new Inventory(SafeBoxCapacity);
    }

    /// Speed multiplier while adrenaline is running.
    [Export] public float AdrenalineBoost { get; set; } = 0.35f;

    public float AdrenalineRemaining { get; private set; }
    public bool AdrenalineActive => AdrenalineRemaining > 0.0f;

    /// Spends the cheapest carried item that would do something right now, and
    /// returns what it cost — which is exactly its extraction value, because the
    /// backpack holds health and money in the same slots.
    ///
    /// Cheapest first, and only if it helps: the tinned food goes before the
    /// medkit, and neither is spent at full health. Nothing here ever reaches
    /// for the serum, because pure cargo is not usable at any price.
    public int TryUseBest()
    {
        int best = -1;
        int bestValue = int.MaxValue;

        for (int i = 0; i < Backpack.EntryCount; i++)
        {
            ItemResource item = Backpack.ItemAt(i);
            if (!item.IsUsable || item.Value >= bestValue || !WouldHelp(item))
                continue;

            best = i;
            bestValue = item.Value;
        }

        if (best < 0)
            return 0;

        ItemResource chosen = Backpack.ItemAt(best);
        if (!Use(chosen))
            return 0;

        Backpack.RemoveOne(best);
        ItemUsed?.Invoke(chosen.ItemName);
        return chosen.Value;
    }

    /// How far a throw lands, along the direction the player is facing. Fixed
    /// rather than aimed at the nearest crowd: a thrown item the player cannot
    /// predict the landing of is one they will not spend.
    [Export] public float ThrowRange { get; set; } = 8.0f;

    /// Throws the cheapest thing in the bag that acts on the world, and returns
    /// what it cost. Its own verb because a heal and a grenade on one key is a
    /// grenade thrown at the wrong moment.
    public int TryThrow()
    {
        int best = -1;
        int bestValue = int.MaxValue;

        for (int i = 0; i < Backpack.EntryCount; i++)
        {
            ItemResource item = Backpack.ItemAt(i);
            if (!item.IsThrowable || item.Value >= bestValue)
                continue;

            best = i;
            bestValue = item.Value;
        }

        if (best < 0 || _horde == null)
            return 0;

        ItemResource chosen = Backpack.ItemAt(best);
        Vector2 aim = Facing == Vector2.Zero ? Vector2.Down : Facing;
        Vector3 landing = GlobalPosition + new Vector3(aim.X, 0.0f, aim.Y) * ThrowRange;
        landing.Y = 0.0f;

        int killed = 0;

        switch (chosen.Effect)
        {
            case ItemEffect.Explosive:
                killed = _horde.Detonate(landing, chosen.EffectRadius * Mods.AreaScale, chosen.EffectAmount);
                GD.Print($"threw {chosen.ItemName}: {killed} killed");
                break;

            case ItemEffect.Incendiary:
                // Zero, and honestly so. Burning ground kills over seven seconds
                // and the horde walks through it the whole time, so any number
                // attributed here would be "kills that happened afterwards"
                // wearing the throw's name.
                _horde.Hazards.Add(landing, chosen.EffectRadius * Mods.AreaScale,
                                   chosen.EffectAmount, chosen.EffectDuration);
                break;

            default:
                return 0;
        }

        Backpack.RemoveOne(best);
        ItemThrown?.Invoke(chosen.ItemName, killed);
        return chosen.Value;
    }

    /// Whether spending something would currently do anything at all. Asked by
    /// the touch controls to dim the use button — the same question TryUseBest
    /// answers by returning zero, but asked before the tap rather than after.
    public bool HasUsableItem
    {
        get
        {
            for (int i = 0; i < Backpack.EntryCount; i++)
            {
                ItemResource item = Backpack.ItemAt(i);
                if (item.IsUsable && WouldHelp(item))
                    return true;
            }

            return false;
        }
    }

    /// How many throwables are in the bag, for the readout. A tactical item the
    /// player has to open a menu to count is one they forget they have.
    public int ThrowableCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Backpack.EntryCount; i++)
            {
                if (Backpack.ItemAt(i).IsThrowable)
                    total += Backpack.CountAt(i);
            }

            return total;
        }
    }

    private bool WouldHelp(ItemResource item) => item.Effect switch
    {
        ItemEffect.Heal => Health < MaxHealth,
        ItemEffect.Ammo => _weapons?.WantsAmmo ?? false,
        ItemEffect.Adrenaline => !AdrenalineActive,
        _ => false,
    };

    private bool Use(ItemResource item)
    {
        switch (item.Effect)
        {
            case ItemEffect.Heal:
                Heal(item.EffectAmount);
                return true;

            case ItemEffect.Ammo:
                return _weapons?.AddReserve(Mathf.RoundToInt(item.EffectAmount)) > 0;

            case ItemEffect.Adrenaline:
                AdrenalineRemaining = item.EffectAmount;
                return true;

            default:
                return false;
        }
    }

    /// Moves one unit of the most valuable backpack item into the safe box.
    /// Returns what it was worth, or 0 if nothing moved.
    ///
    /// One unit per press on purpose: securing a haul costs real seconds while
    /// the horde closes, which is the decision the safe box exists to create.
    /// The profile this run belongs to, for the one question the run asks of it.
    ///
    /// Looked up lazily rather than injected: `Player` is built before
    /// `MetaManager` in `BuildMain` and a reference taken in `_Ready` would be
    /// null. Null is a legitimate answer — several probes build a player with no
    /// meta manager at all — and `CollectionBook.Wanted` treats a null profile as
    /// "nothing collected yet", which errs toward protecting a piece.
    private Profile? RunProfile =>
        (_meta ??= GetParent()?.GetNodeOrNull<MetaManager>("MetaManager"))?.Profile;

    private MetaManager? _meta;

    /// True for a collection piece this profile has not banked yet.
    public bool StillNeeded(ItemResource item) => CollectionBook.Wanted(RunProfile, item.ItemName);

    /// How far along the set an item belongs to is, for a readout.
    public (int Found, int Total, string Name) CollectionProgress(string itemName) =>
        CollectionBook.Progress(RunProfile, itemName);

    public int TrySecureBest()
    {
        // A piece of an unfinished set goes in the safe box before anything else,
        // whatever it is worth. The safe box is what survives dying, and a wedding
        // ring worth forty credits is the only thing in the bag that cannot be
        // bought again.
        int index = Backpack.MostValuableIndex(StillNeeded);
        if (index < 0)
            return 0;

        ItemResource item = Backpack.ItemAt(index);
        if (SafeBox.TryAdd(item, 1) == 0)
            return 0;

        Backpack.RemoveOne(index);
        return item.Value;
    }

    /// Applies the gear the player walked in with. Called before the run starts,
    /// while the inventories are still empty — they are rebuilt here because a
    /// backpack's size is something gear decides, not the scene.
    public void ApplyGear(float health, float armour, float speed, int carry, int safeBox)
    {
        MaxHealth += health;
        Armour += armour;
        MoveSpeed += speed;
        CarryCapacity += carry;
        SafeBoxCapacity += safeBox;

        Health = MaxHealth;
        Backpack = new Inventory(CarryCapacity);
        SafeBox = new Inventory(SafeBoxCapacity);
    }

    /// The rules the gear grants before the first level-up.
    ///
    /// Added to whatever the run already holds rather than assigned, and applied
    /// in the same pass as the stats. `RunModifiers` has a `Reset()` that nothing
    /// currently calls; if anything ever does, it has to happen before this and
    /// not after — the failure mode is a piece of equipment that works until the
    /// player takes their first upgrade and then quietly stops, which no exit
    /// code would report.
    ///
    /// Every argument is what the gear *adds*, so zero is the neutral value for
    /// all six — including area, whose modifier is a multiplier neutral at 1.
    /// Passing a RunModifiers here instead was the first shape and it made three
    /// pieces granting nothing add up to a triple-size blast.
    /// What the equipped trinket starts the run holding.
    ///
    /// Its own call rather than four more arguments on `ApplyGearRules`, which
    /// already takes six and whose six are all rules about the weapon. These are
    /// objects in the world; mixing them in would make the longer signature the
    /// place where somebody eventually passes `regen` where `chill` goes.
    ///
    /// Chill compounds rather than sums, matching how the card stacks — a
    /// trinket granting 17% on top of a run that has taken two chill picks has to
    /// land on the same curve, or gear and growth would disagree about what a
    /// stack is worth.
    public void ApplyGearKit(int orbit, int shockwave, float chain, float chill)
    {
        Mods.OrbitBlades += orbit;
        Mods.PulseStacks += shockwave;
        Mods.ChainChance += chain;

        if (chill > 0.0f)
            Mods.Chill = 1.0f - (1.0f - Mods.Chill) * (1.0f - chill);
    }

    public void ApplyGearRules(int pierce, float area, float thorns, float regen,
                               float knockback, float dodge)
    {
        Mods.Pierce += pierce;
        Mods.AreaScale += area;
        Mods.Thorns += thorns;
        Mods.Regen += regen;
        Mods.Knockback += knockback;
        Mods.Dodge += dodge;
    }

    /// In-run upgrades. Health is granted as current as well as maximum: a pick
    /// that only raises the ceiling is worth nothing at the moment it is offered,
    /// which is exactly when the player is deciding whether it saves them.
    /// This survivor's body, in this survivor's colours, holding whatever is
    /// in hand. One place, because it is built twice — once at `_Ready` and again
    /// every time the carried silhouette changes — and two copies would drift the
    /// first time a character gained a proportion of its own.
    private BodyMeshLibrary.Build BodySpec()
    {
        CharacterResource who = CharacterBook.Load(GameSession.Character);
        return BodyMeshLibrary.ForPlayer(BodyHeight, _held, who.Torso, who.Limb, who.Head);
    }

    /// Becomes whoever the base screen picked.
    ///
    /// Applied in `_Ready` before anything reads the numbers: `Health` is set
    /// from `MaxHealth` further down, `WeaponHandler` sizes nothing off this, and
    /// the backpack takes its capacity here. Applying it later would leave a
    /// survivor with the Warden's health bar and the Drifter's hundred points in
    /// it.
    ///
    /// **The exports stay as the fallback.** They hold the Drifter's numbers, so
    /// a scene opened without a session — which is most probes — behaves exactly
    /// as it did before the roster existed.
    private void ApplyCharacter()
    {
        CharacterResource who = CharacterBook.Load(GameSession.Character);

        MaxHealth = who.MaxHealth;
        MoveSpeed = who.MoveSpeed;
        CarryCapacity = who.CarryCapacity;
        BodyHeight = who.BodyHeight;

        // The ability, as a head start on the deck rather than a mechanic of its
        // own. Added rather than assigned: a trinket that granted a blade before
        // this ran would otherwise be silently thrown away.
        Mods.OrbitBlades += who.StartingBlades;
        Mods.Chill = Mathf.Max(Mods.Chill, who.StartingChill);
        Mods.LootValueScale *= who.LootValueScale;
        Mods.SearchRadiusBonus += who.SearchRadiusBonus;
    }

    public void AddMaxHealth(float amount)
    {
        MaxHealth += amount;
        Health = Mathf.Min(MaxHealth, Health + amount);
    }

    public void AddArmour(float amount) => Armour += amount;
    public void AddMoveSpeedFraction(float fraction) => MoveSpeed *= 1.0f + fraction;
    public void AddSearchSpeedFraction(float fraction) => SearchSpeed += fraction;

    /// Damage is ignored once dead, so a horde landing several hits in the same
    /// tick cannot emit Died more than once.
    public void TakeDamage(float amount) => ApplyDamage(Mitigate(amount));

    /// Contact arrives as a rate, so armour is subtracted from the rate rather
    /// than from each tick's slice — otherwise mitigation would depend on the
    /// physics tick rate, which is not a thing the player can see or choose.
    ///
    /// Dodge is rolled per tick rather than per hit, for the same reason: there
    /// are no hits, only a rate. A tenth of dodge therefore removes a tenth of
    /// the damage over any window long enough to matter, which is what the card
    /// promises and what a per-hit roll would only approximate.
    public void TakeContactDamage(float damagePerSecond, float delta)
    {
        if (Mods.Dodge > 0.0f && NextFloat() < Mods.Dodge)
            return;

        ApplyDamage(Mitigate(damagePerSecond) * delta);
    }

    private ulong _rng = 0x9E3779B97F4A7C15UL;

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }

    /// Twenty percent always gets through. Armour that can reach zero turns the
    /// weakest variant into scenery, and a horde you can stand in is not a horde.
    private float Mitigate(float amount) =>
        amount <= 0.0f ? 0.0f : Mathf.Max(amount - Armour, amount * 0.2f);

    /// Damage taken since somebody last asked, cleared by asking.
    ///
    /// A signal per application would fire sixty times a second: contact damage
    /// arrives as a per-tick slice of a rate, so "was I hit" is not an event the
    /// player character can answer — only "how much, lately". The reader decides
    /// what is worth reacting to, which keeps the threshold next to the feedback
    /// rather than buried in here.
    ///
    /// Reading clears it, so it has exactly one owner: `SoundDirector`. Anything
    /// else that wants to react to damage watches Health instead — a second
    /// consumer would take turns with the first and each would see about half of
    /// what happened, which is a bug that presents as feedback that sometimes
    /// works.
    private float _damageSincePoll;

    public float ConsumeDamageTaken()
    {
        float taken = _damageSincePoll;
        _damageSincePoll = 0.0f;
        return taken;
    }

    private void ApplyDamage(float amount)
    {
        if (!IsAlive || amount <= 0.0f)
            return;

        Health = Mathf.Max(0.0f, Health - amount);
        _damageSincePoll += amount;

        // Six times the fraction of maximum health this took, which is what
        // makes a hit look like a hit and a rate look like a rate. See HurtFlash.
        HurtFlash = Mathf.Min(1.0f, HurtFlash + amount / Mathf.Max(1.0f, MaxHealth) * 6.0f);

        if (Health <= 0.0f)
            EmitSignal(SignalName.Died);
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);

    public override void _PhysicsProcess(double delta)
    {
        _input.Update(GlobalPosition);

        if (AdrenalineRemaining > 0.0f)
            AdrenalineRemaining = Mathf.Max(0.0f, AdrenalineRemaining - (float)delta);

        if (Mods.Regen > 0.0f && IsAlive)
            Heal(Mods.Regen * (float)delta);

        Vector2 move = Steer(_input.Move, (float)delta);
        float speed = AdrenalineActive ? MoveSpeed * (1.0f + AdrenalineBoost) : MoveSpeed;
        var desired = new Vector3(move.X, 0.0f, move.Y) * speed;

        // Frame-rate-independent damping (godot.md:50): exponential decay toward
        // the target, never a fixed fraction per tick — the latter changes feel
        // whenever the tick rate does.
        float t = 1.0f - Mathf.Exp(-AccelerationRate * (float)delta);
        Velocity = new Vector3(
            Mathf.Lerp(Velocity.X, desired.X, t),
            0.0f,
            Mathf.Lerp(Velocity.Z, desired.Z, t));

        MoveAndSlide();

        // Planted after the move, never before.
        //
        // The floor collider is a flat box and the simulation is two-dimensional
        // — see `Terrain`. `MoveAndSlide` resolves against that flat floor, and
        // only then is the body lifted onto the visible ground. Doing it the
        // other way round means sliding along a surface the physics does not
        // have, which produces a character that skates and occasionally falls
        // through.
        GlobalPosition = new Vector3(
            GlobalPosition.X,
            Terrain.Height(GlobalPosition.X, GlobalPosition.Z),
            GlobalPosition.Z);

        UpdateFacing();
        UpdateBody((float)delta);

        if (_input.SecurePressed)
            TrySecureBest();

        if (_input.UsePressed)
            TryUseBest();

        if (_input.ThrowPressed)
            TryThrow();

        if (_input.SwapPressed)
            _weapons?.SwapWeapon();

        if (_input.DropPressed)
            TryDropWorst();
    }

    /// Turns the stick into a world-space direction to travel in.
    ///
    /// Under `TurnToSteer` the horizontal axis is not movement at all: it spends
    /// itself on the camera rig, and the vertical axis advances along whatever
    /// direction that leaves the view pointing. Two keys, one heading — which is
    /// why the scheme is worth the disruption. Under the old scheme the player
    /// could be running north while looking east, and every piece of art in the
    /// game had to work from every angle at once because any angle was reachable
    /// without turning.
    ///
    /// **This breaks every automated driver in the repository**, and that is not
    /// a side effect to be worked around — it is the same break a player feels.
    /// A driver that decomposes a world direction into four keys will hold the
    /// turn key forever and never advance. See `test/BotDrive.cs`, which is the
    /// one place the conversion back is written.
    ///
    /// Returns the XZ direction to move in, unnormalised — the stick's magnitude
    /// is a throttle and a virtual stick pushed halfway should walk.
    public Vector2 Steer(Vector2 stick, float step)
    {
        if (!TurnToSteer || _rig == null)
            return stick;

        // Negated: pushing right turns the view clockwise seen from above, which
        // is the direction the world appears to swing left. Getting this backwards
        // is not subtle and is also not caught by any test that only checks
        // whether the player moved.
        if (stick.X != 0.0f)
            _rig.Turn(-stick.X * Mathf.DegToRad(TurnRateDegrees) * step);

        // Negated for the same reason `move_up` is −Y: the stick's vertical axis
        // is screen-space and points down.
        return _rig.Forward() * -stick.Y;
    }

    /// Throws away one unit of the worst thing being carried.
    ///
    /// Worst by value per bulk, which is a different question from the one
    /// `TrySecureBest` asks. Securing is "what do I most want to keep"; dropping
    /// is "what is costing me the most room for the least return", and four boxes
    /// of rifle rounds answer the second differently from the first.
    ///
    /// One unit rather than the whole stack. Standing over a cache the player
    /// wants exactly enough room for what is in front of them, and a key that
    /// dumped a whole stack would cost more than it had to every time.
    ///
    /// Returns what was thrown away, so a readout can say so — a silent drop
    /// under pressure is indistinguishable from a key that did nothing.
    public int TryDropWorst()
    {
        // And it is the last thing thrown away.
        //
        // Rarely load-bearing: by value per bulk a piece is 65 to 160 against
        // rifle rounds at 18, so the drop key was not reaching it in an ordinary
        // bag. It reaches it in a bag holding nothing but dear things, which is
        // the situation where being wrong cannot be undone.
        int index = Backpack.LeastValuableIndex(StillNeeded);
        if (index < 0)
            return 0;

        ItemResource item = Backpack.ItemAt(index);
        return Backpack.RemoveOne(index) ? item.Value : 0;
    }

    /// Places the body and walks its legs.
    ///
    /// The facing is the view rather than the direction of travel, which is the
    /// point of turn-and-advance: backing away from something while still looking
    /// at it is a thing the player does, and the body has to do it too. The gait
    /// comes from actual speed, so the legs stop when the player does.
    private void UpdateBody(float delta)
    {
        if (_body == null)
            return;

        // Before the update, so a rebuilt body is placed on the same frame it
        // was made. After it, the new body sits at the origin for one frame —
        // which at sixty frames a second is a single flicker of a player
        // standing in the middle of the map, and is exactly the kind of thing
        // that gets seen once and never reproduced.
        CarryChanged();

        var flatVelocity = new Vector2(Velocity.X, Velocity.Z);
        float yaw = Mathf.Atan2(-Facing.X, -Facing.Y);

        _body.Update(GlobalPosition, yaw, flatVelocity.Length(), delta, HurtFlash);
        HurtFlash = Mathf.Max(0.0f, HurtFlash - FlashFade * delta);
    }

    /// Lights the body white for a moment after taking a hit.
    ///
    /// Raised in proportion to the fraction of maximum health a single
    /// application took, and decayed on a timer — which is what separates a hit
    /// from a rate. Contact damage arrives as a per-tick slice of a rate: at five
    /// damage a second against a hundred maximum, each tick adds half a
    /// percent of a flash while the decay removes six percent, so standing in a
    /// crowd glows faintly and a shotgun to the chest flashes white. A flash
    /// triggered by "did health drop" would be on permanently in a horde, which
    /// stops being feedback and becomes the way the game looks.
    public float HurtFlash { get; private set; }

    /// Fractions of a second for a full flash to fade. Short enough that two
    /// hits read as two.
    private const float FlashFade = 4.0f;

    /// The solid body, or null on the sprite path. Only a probe asks.
    public SoloBody? Body => _body;

    /// One sprite direction, mirrored at runtime. Generators cannot reliably draw
    /// a specific facing (SKILL.md:109), so paying for a mirrored set buys
    /// nothing a flip does not.
    ///
    /// Under `TurnToSteer` the facing is the view, full stop — not the stick and
    /// not the mouse. It has to be: the player standing still and turning is
    /// aiming, and a facing that only updated while moving would leave a swing at
    /// empty air going wherever the last step went. The mouse gave up aiming for
    /// this and got turning instead; `CameraRig._UnhandledInput` says why.
    private void UpdateFacing()
    {
        if (TurnToSteer && _rig != null)
        {
            Facing = _rig.Forward();
            if (Mathf.Abs(Facing.X) > 0.05f)
                _sprite.FlipH = Facing.X < 0.0f;
            return;
        }

        Vector2 facing = _input.Aim != Vector2.Zero ? _input.Aim : _input.Move;
        if (facing == Vector2.Zero)
            return;

        Facing = facing.Normalized();
        if (Mathf.Abs(facing.X) > 0.05f)
            _sprite.FlipH = facing.X < 0.0f;
    }
}
