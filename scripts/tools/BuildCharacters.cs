using Godot;

/// Writes the survivor roster to resources/characters/*.tres.
///
///   godot --headless --script scripts/tools/BuildCharacters.cs
///
/// Three, and they are built so that no two of them want the same run. The test
/// that matters is not "are the numbers different" — any three sets are — but
/// "does each one make a different thing worth doing", because a survivor that is
/// merely stronger is a difficulty setting with a name on it.
public partial class BuildCharacters : SceneTree
{
    private const string OutputDir = "res://resources/characters";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        CharacterResource[] roster =
        {
            // **The names changed and the numbers did not, and the order is why
            // both were safe.** `CharacterBook.Order` is a fixed list and the
            // profile stores an *index* into it, so renaming entry zero from
            // Drifter to RIN keeps every saved profile pointing at the same
            // survivor with the same hundred health. Adding to the end is
            // likewise free. Reordering would not be, and nothing here does it.
            //
            // The five are PROJECT LAST DAWN's cast, whose models and design
            // sheets arrived together — see `assets/models/SOURCE.md`. RIN, MIKA
            // and AKIRA inherit the Drifter's, the Courier's and the Warden's
            // numbers exactly; SORA and YUNA take two designs `CHARACTERS.md`
            // had already reasoned through and costed, under new names.

            // **Every number here is what `Player` shipped with, to the digit.**
            //
            // That is not a placeholder, it is the whole reason the other four
            // can exist safely: eleven phases of balance work, forty-odd probes
            // and every number in the shop were tuned against this survivor, and
            // a "default" that improved on it would have re-balanced the game as
            // a side effect of adding a roster.
            new()
            {
                CharacterName = "RIN",
                Role = "character.rin.role",
                Blurb = "character.rin.blurb",
                MaxHealth = 100.0f,
                MoveSpeed = 6.0f,
                CarryCapacity = 20,
                BodyHeight = 2.20f,
                PortraitPath = "res://assets/ui/portraits/rin.png",

                // **No `BakedBodyPath`, and the bake is still loaded.**
                // `resources/bodies/rin.res` is where a survivor's body goes and
                // `BodyBakes` looks there, so a model arrives as a file rather
                // than as an edit to this line.
                OpensAfter = 0,
            },

            // Gets in, takes everything, does not stay.
            //
            // Eight more bulk and a wider reach on a crate, so a full sweep of a
            // map fits in one trip — against twenty per cent less health, which
            // in this game is not a health bar so much as a number of mistakes.
            // The run is decided by route: it can afford to go deep and cannot
            // afford to be caught out there.
            //
            // The loot multiplier is small on purpose. At 1.15 it is a reason to
            // pick the character; at 1.4 it would be the only correct pick and
            // the roster would collapse to one.
            //
            // **The ability and the design agree, which was luck.** These were
            // the Courier's numbers — a scavenger who carries more and searches
            // wider — and MIKA is the recon character whose kit is a drone and a
            // tablet. A drone that finds crates *is* `SearchRadiusBonus`.
            new()
            {
                CharacterName = "MIKA",
                Role = "character.mika.role",
                Blurb = "character.mika.blurb",
                MaxHealth = 80.0f,
                MoveSpeed = 6.6f,
                CarryCapacity = 28,

                // 158 cm on the design sheet against RIN's 172, at the same 1.28x
                // this game draws survivors at. The height spread is the point
                // rather than a detail: the production spec asks that five
                // survivors be identifiable in flat grey with the names hidden,
                // and 2.02 against 2.24 is most of how that is true.
                BodyHeight = 2.02f,
                PortraitPath = "res://assets/ui/portraits/mika.png",

                LootValueScale = 1.15f,
                SearchRadiusBonus = 0.9f,

                Torso = new Color(0.20f, 0.42f, 0.46f),
                Limb = new Color(0.24f, 0.30f, 0.34f),
                Head = new Color(0.74f, 0.62f, 0.50f),

                OpensAfter = 3,
            },

            // Stands somewhere and makes the crowd come to it.
            //
            // Forty per cent more health, a blade already turning and cold ground
            // underfoot from the first second — against six fewer bulk and a
            // slower walk, so it cannot sweep a map and has to choose a corner of
            // it. The two starting modifiers are the point: they are the head
            // start on a strategy the deck can then be built around, rather than
            // a bonus that sits on top of whatever the player was doing anyway.
            //
            // Smaller bag *and* slower is two costs, and it needs both. With only
            // one, the extra health made it the safe pick for a bad player and
            // the strong pick for a good one, which is the definition of a
            // difficulty setting.
            //
            // **`StartingChill` is the one place a name and a mechanic disagree.**
            // These were the Warden's numbers and the chill was "the floor is
            // cold"; AKIRA is a berserker with a greatsword, and slowing the
            // crowd is not what a berserker does. `IgniteChance` or `Lifesteal`
            // would fit the character, and either is a balance change to a
            // survivor eleven phases of work were done against — so the numbers
            // stay and the mismatch is written down instead of quietly fixed. The
            // turning blade, at least, is a greatsword.
            new()
            {
                CharacterName = "AKIRA",
                Role = "character.akira.role",
                Blurb = "character.akira.blurb",
                MaxHealth = 140.0f,
                MoveSpeed = 5.3f,
                CarryCapacity = 14,
                BodyHeight = 2.24f,
                PortraitPath = "res://assets/ui/portraits/akira.png",

                StartingBlades = 1,
                StartingChill = 0.25f,

                Torso = new Color(0.30f, 0.28f, 0.44f),
                Limb = new Color(0.24f, 0.24f, 0.30f),
                Head = new Color(0.70f, 0.58f, 0.46f),

                OpensAfter = 8,
            },

            // Everything else asks how long you stay. This asks how fast you can
            // be gone.
            //
            // The run's tension is an extraction multiplier climbing 1.0 to 3.0
            // against a horde that caps at 160. Every survivor above answers it
            // by getting stronger; this one answers it by being somewhere else.
            // 7.1 m/s is a fifth faster than RIN and faster than a runner's 4.6
            // by a margin that makes breaking contact a decision rather than a
            // hope.
            //
            // Seventy health is the lowest in the game and it is the whole cost:
            // two brute contacts and one mistake. Dodge 0.12 is not compensation
            // for that — it is rolled per tick against contact damage, so it
            // removes about a tenth of a rate this survivor cannot afford to be
            // inside at all.
            //
            // Designed in `CHARACTERS.md` as the Scout and named for SORA here,
            // whose kit is six flying swords and whose movement is riding one.
            // Its bad map is Cold Storage: speed buys nothing in a room.
            new()
            {
                CharacterName = "SORA",
                Role = "character.sora.role",
                Blurb = "character.sora.blurb",
                MaxHealth = 70.0f,
                MoveSpeed = 7.1f,
                CarryCapacity = 17,
                BodyHeight = 2.15f,
                PortraitPath = "res://assets/ui/portraits/sora.png",

                StartingDodge = 0.12f,

                Torso = new Color(0.34f, 0.26f, 0.48f),
                Limb = new Color(0.22f, 0.20f, 0.30f),
                Head = new Color(0.86f, 0.74f, 0.64f),

                // Twelve rather than the five `CHARACTERS.md` costed, and the
                // reason is the ladder rather than the design: the roster opens
                // at 0, 3, 8, 12 and 16 now, so each survivor arrives a distance
                // from the last. Five would have put this one before AKIRA while
                // sitting after it in the list, which is a menu that unlocks out
                // of order.
                OpensAfter = 12,
            },

            // The one survivor that gets stronger for being hit.
            //
            // Twenty-five per cent more health, a wound that closes on its own
            // and a crowd that hurts itself on contact — against a slower walk
            // and four fewer bulk. Thorns and regen are the only pair in
            // `RunModifiers` that reward standing *inside* the horde rather than
            // beside it, which is a strategy nothing else in the roster starts.
            //
            // Regen 0.5 is half a health point a second, which is nothing in a
            // fight and is a fight's worth over a two-minute run. That is the
            // shape it should have: it does not save a mistake, it removes the
            // slow bleed that decides whether a good run reaches the exit.
            //
            // Designed in `CHARACTERS.md` as the Revenant, and named for YUNA,
            // whose kit is a healing gun and a medical drone.
            new()
            {
                CharacterName = "YUNA",
                Role = "character.yuna.role",
                Blurb = "character.yuna.blurb",
                MaxHealth = 125.0f,
                MoveSpeed = 5.6f,
                CarryCapacity = 16,
                BodyHeight = 2.08f,
                PortraitPath = "res://assets/ui/portraits/yuna.png",

                StartingThorns = 0.30f,
                StartingRegen = 0.5f,

                Torso = new Color(0.24f, 0.44f, 0.40f),
                Limb = new Color(0.26f, 0.32f, 0.30f),
                Head = new Color(0.90f, 0.80f, 0.66f),

                OpensAfter = 16,
            },
        };

        foreach (CharacterResource one in roster)
        {
            string path = $"{OutputDir}/{one.CharacterName.ToLower().Replace(' ', '_')}.tres";
            Error err = ResourceSaver.Save(one, path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }

            GD.Print($"Saved {path}");
        }

        return true;
    }
}
