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
            // **Every number here is what `Player` shipped with, to the digit.**
            //
            // That is not a placeholder, it is the whole reason the other two can
            // exist safely: eleven phases of balance work, forty-odd probes and
            // every number in the shop were tuned against this survivor, and a
            // "default" that improved on it would have re-balanced the game as a
            // side effect of adding a roster.
            new()
            {
                CharacterName = "Drifter",
                Blurb = "no edges, no gaps; everything the shop sells is priced for this one",
                MaxHealth = 100.0f,
                MoveSpeed = 6.0f,
                CarryCapacity = 20,
                BodyHeight = 2.2f,

                // **A downloaded model, and the first authored body in this game
                // that beat the procedural one.** Two rounds lost: seven three.js
                // humanoids in the horde, and the Drifter's own predecessor cut
                // from a blend — a small head, arms welded to the torso, no hands
                // and none of the kit `CHARACTERS.md` says a survivor wears.
                //
                // This one arrives wearing it. 23,822 triangles is forty times a
                // horde variant and costs nothing here, because the player is one
                // body on screen and not a hundred and fifty — the tier `ART.md`
                // calls free and this game had never used. `mixamorig_*` bone
                // names classify cleanly, so the shader's hip-and-shoulder swing
                // lands on the right vertices and it walks rather than scatters.
                //
                // The other two survivors stay procedural on purpose: the roster
                // is meant to read as three people and one bake plus two
                // `MeshBuilder` bodies is the comparison that says whether the
                // bake is worth extending. See README.
                //
                // **No `BakedBodyPath`, and the bake is still loaded.**
                // `resources/bodies/drifter.res` is where a survivor's body goes
                // and `BodyBakes` looks there — so a survivor's model arrives as
                // a file rather than as an edit to this line, and the same is
                // true of the Courier and the Warden the moment either has one.
                OpensAfter = 0,
            },

            // Gets in, takes everything, does not stay.
            //
            // Eight more bulk and a wider reach on a crate, so a full sweep of a
            // map fits in one trip — against twenty per cent less health, which
            // in this game is not a health bar so much as a number of mistakes.
            // The Courier's run is decided by route: it can afford to go deep and
            // cannot afford to be caught out there.
            //
            // The loot multiplier is small on purpose. At 1.15 it is a reason to
            // pick the character; at 1.4 it would be the only correct pick and the
            // roster would collapse to one.
            new()
            {
                CharacterName = "Courier",
                Blurb = "carries half again as much and cannot take a hit",
                MaxHealth = 80.0f,
                MoveSpeed = 6.6f,
                CarryCapacity = 28,
                BodyHeight = 2.1f,

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
            // one, the extra health made it the safe pick for a bad player and the
            // strong pick for a good one, which is the definition of a difficulty
            // setting.
            new()
            {
                CharacterName = "Warden",
                Blurb = "holds ground; a blade already turning, and the floor is cold",
                MaxHealth = 140.0f,
                MoveSpeed = 5.3f,
                CarryCapacity = 14,
                BodyHeight = 2.25f,

                StartingBlades = 1,
                StartingChill = 0.25f,

                Torso = new Color(0.30f, 0.28f, 0.44f),
                Limb = new Color(0.24f, 0.24f, 0.30f),
                Head = new Color(0.70f, 0.58f, 0.46f),

                OpensAfter = 8,
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
