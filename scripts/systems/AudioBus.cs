using Godot;

/// The master bus, and the one effect on it.
///
/// Every sound in the game plays into the default bus with nothing between it
/// and the speakers. The mix has held its headroom by arithmetic: the sound
/// director attenuates by −9 dB, the music by −14, and the four music layers at
/// full were measured peaking together at 0.41. That is a real measurement and
/// it is not a guarantee — it covers the music and nothing else. The loudest
/// moment this game can produce is the fourteen-voice SFX ring firing at once,
/// which at the loudest clip and the loudest trim sums to 5.63. **The worst case
/// is 6.04, six times over unity**, and four simultaneous impacts already pass
/// it. Nothing in the mix had ever been asked what it sums to.
///
/// A hard limiter is the cheap insurance. Below the ceiling it does nothing at
/// all, so the mix that was tuned by ear is the mix that plays; above it, the
/// waveform is held rather than wrapped, which is the difference between a loud
/// moment and a click.
///
/// **Installed here rather than in a `default_bus_layout.tres`.** Godot loads
/// that file automatically if it exists, which sounds like the tidier answer and
/// is the wrong one for this project: every other piece of configuration here is
/// generated from code with the reasoning written next to it, and a hand-authored
/// resource is a decision nothing records. It is also a format nothing else in
/// the repository uses.
public static class AudioBus
{
    /// Where the waveform is held, in dB.
    ///
    /// Godot's own default, and its reasoning is worth keeping: a true peak of
    /// exactly 0 can still produce inter-sample peaks above it once the signal
    /// is reconstructed, which distorts on some hardware. Three tenths of a
    /// decibel is inaudible and buys that margin.
    private const float CeilingDb = -0.3f;

    /// Seconds for the gain reduction to let go.
    ///
    /// Short. This is protecting against a burst of impacts rather than riding a
    /// sustained level, and a long release after one loud frame would duck the
    /// music for a noticeable moment afterwards — which is the artefact that
    /// makes a limiter audible, and an audible limiter is a mix change nobody
    /// asked for.
    private const float Release = 0.12f;

    private static bool _installed;

    /// Adds the limiter to the master bus, once per process.
    ///
    /// Guarded because `AudioServer` is global and the thing that calls this is
    /// not: a run and the base each build their own sound director, and every
    /// scene change would otherwise stack another limiter on the bus. Ten
    /// limiters in series is not ten times the protection, it is nine
    /// unnecessary gain stages.
    public static void Install()
    {
        if (_installed)
            return;

        _installed = true;

        // Already there is also a reason not to add one — a headless probe that
        // reloads the scene several times comes through here more than once, and
        // the static flag above only covers a single process.
        for (int i = 0; i < AudioServer.GetBusEffectCount(0); i++)
        {
            if (AudioServer.GetBusEffect(0, i) is AudioEffectHardLimiter)
                return;
        }

        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter
        {
            CeilingDb = CeilingDb,
            Release = Release,

            // No pre-gain. The mix was tuned by ear at the volumes the directors
            // set, and a limiter that also made everything louder would be a
            // mastering decision smuggled in as a safety net.
            PreGainDb = 0.0f,
        });
    }

    /// The master bus's current peak, in dB.
    ///
    /// The louder of the two channels. Godot reports −200 for silence rather
    /// than negative infinity, so a caller comparing against a ceiling needs no
    /// special case for "nothing is playing".
    ///
    /// Worth knowing before reaching for it: the **headless driver processes no
    /// audio**, so this reads silence forever under `--headless` and a probe
    /// built on it would pass against a bus with no limiter and no sound in it.
    /// `AudioProbe` computes the mix instead.
    public static float PeakDb() =>
        Mathf.Max(AudioServer.GetBusPeakVolumeLeftDb(0, 0), AudioServer.GetBusPeakVolumeRightDb(0, 0));
}
