using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NetCoreAudio;

namespace AVAMissile.Engine;

public class SoundManager : IDisposable
{
    private readonly string _soundsPath;
    private readonly Dictionary<string, Loop> _loops = new();

    public bool IsMuted { get; set; } = false;

    /// <summary>Set during attract mode: the game is silent without touching the player's mute setting.</summary>
    public bool IsSilenced { get; set; }

    private bool IsQuiet => IsMuted || IsSilenced;

    public SoundManager()
    {
        _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
    }

    public void PlayLaunch() => PlaySound("Launch.wav");
    public void PlayBoom() => PlaySound("Boom.wav");
    public void PlayCityBoom() => PlaySound("CityBoom.wav");
    public void PlayBaseBoom() => PlaySound("BaseBoom.wav");
    public void PlayEmpty() => PlaySound("Empty.wav");
    public void PlayLow() => PlaySound("Low.wav");
    public void PlayAlert() => PlaySound("Alert.wav");
    public void PlayTallyMissile() => PlaySound("TallyMissile.wav");
    public void PlayTallyCity() => PlaySound("TallyCity.wav");
    public void PlayBonusCity() => PlaySound("BonusCity.wav");
    public void PlayTheEnd() => PlaySound("TheEnd.wav");
    public void PlayFlyerBoom() => PlaySound("FlyerBoom.wav");

    /// <summary>The drone of a bomber or satellite crossing the sky.</summary>
    public void StartFlyer() => StartLoop("Flyer.wav");
    public void StopFlyer() => StopLoop("Flyer.wav");

    /// <summary>The whine of a smart bomb on its way down.</summary>
    public void StartSmartBomb() => StartLoop("SmartBomb.wav");
    public void StopSmartBomb() => StopLoop("SmartBomb.wav");

    public void StopAllLoops()
    {
        foreach (var name in _loops.Keys)
            StopLoop(name);
    }

    // A sound that repeats while something is held (the engines) or going on (the bridge hum). Each start
    // gets its own generation, so a loop left over from a quick stop and start ends instead of doubling up.
    private sealed class Loop
    {
        public bool Playing;
        public int Generation;
        public Player? Player;
    }

    private void StartLoop(string filename)
    {
        if (IsQuiet) return;
        if (!_loops.TryGetValue(filename, out var loop))
            _loops[filename] = loop = new Loop();
        if (loop.Playing) return;

        loop.Playing = true;
        RunLoop(filename, loop, ++loop.Generation);
    }

    private async void RunLoop(string filename, Loop loop, int generation)
    {
        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path))
        {
            loop.Playing = false;
            return;
        }

        var player = new Player();
        loop.Player = player;
        try
        {
            while (loop.Playing && generation == loop.Generation)
            {
                await player.Play(path);
                while (player.Playing && loop.Playing && generation == loop.Generation)
                    await Task.Delay(20);
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
        finally
        {
            if (generation == loop.Generation)
                loop.Playing = false;
            else
                StopQuietly(player);
        }
    }

    private void StopLoop(string filename)
    {
        if (!_loops.TryGetValue(filename, out var loop)) return;
        loop.Playing = false;
        if (loop.Player != null)
            StopQuietly(loop.Player);
    }

    private static void StopQuietly(Player player)
    {
        try
        {
            player.Stop();
        }
        catch
        {
            // Silently ignore stop errors
        }
    }

    private async void PlaySound(string filename)
    {
        if (IsQuiet) return;

        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path)) return;

        try
        {
            // Create new player for each sound to allow overlapping
            var player = new Player();
            await player.Play(path);
        }
        catch
        {
            // Silently ignore playback errors
        }
    }

    public void Dispose()
    {
        StopAllLoops();
    }
}
