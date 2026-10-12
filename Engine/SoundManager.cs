using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NetCoreAudio;

namespace AVASinstar.Engine;

/// <summary>The Sinistar's voice.</summary>
public enum Voice
{
    BewareILive,
    IHunger,
    RunCoward,
    IAmSinistar,
    BewareCoward,
    RunRunRun,
    IHungerCoward,
    Scream
}

public class SoundManager : IDisposable
{
    private readonly string _soundsPath;
    private readonly Dictionary<string, Loop> _loops = new();

    // Voice reports queue up so they never talk over each other; one that has waited too long is dropped.
    private readonly Queue<(Voice Line, DateTime Queued)> _voiceQueue = new();
    private bool _voiceBusy;
    private static readonly TimeSpan VoiceStale = TimeSpan.FromSeconds(4);

    public bool IsMuted { get; set; } = false;
    public bool IsVoiceOn { get; set; } = true;

    /// <summary>Set during attract mode: the game is silent without touching the player's mute setting.</summary>
    public bool IsSilenced { get; set; }

    private bool IsQuiet => IsMuted || IsSilenced;

    public SoundManager()
    {
        _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
    }

    public void PlayFire() => PlaySound("Fire.wav");
    public void PlayRockHit() => PlaySound("RockHit.wav");
    public void PlayCrystal() => PlaySound("Crystal.wav");
    public void PlayPickup() => PlaySound("Pickup.wav");
    public void PlayBombLaunch() => PlaySound("BombLaunch.wav");
    public void PlayBombHit() => PlaySound("BombHit.wav");
    public void PlayWorkerBoom() => PlaySound("WorkerBoom.wav");
    public void PlayWarriorBoom() => PlaySound("WarriorBoom.wav");
    public void PlayWarriorFire() => PlaySound("WarriorFire.wav");
    public void PlayShipBoom() => PlaySound("ShipBoom.wav");
    public void PlayPiece() => PlaySound("Piece.wav");
    public void PlayRoar() => PlaySound("Roar.wav");
    public void PlaySinistarBoom() => PlaySound("SinistarBoom.wav");
    public void PlayZone() => PlaySound("Zone.wav");
    public void PlayExtraShip() => PlaySound("ExtraShip.wav");
    public void PlayEmpty() => PlaySound("Empty.wav");

    public void StartThrust() => StartLoop("Thrust.wav");
    public void StopThrust() => StopLoop("Thrust.wav");

    /// <summary>The Sinistar's heartbeat once it lives.</summary>
    public void StartHeartbeat() => StartLoop("Heartbeat.wav");
    public void StopHeartbeat() => StopLoop("Heartbeat.wav");

    public void StopAllLoops()
    {
        foreach (var name in _loops.Keys)
            StopLoop(name);
    }

    public void Say(Voice line)
    {
        if (IsQuiet || !IsVoiceOn) return;

        _voiceQueue.Enqueue((line, DateTime.Now));
        if (!_voiceBusy)
            SpeakQueue();
    }

    public void ClearVoice() => _voiceQueue.Clear();

    private async void SpeakQueue()
    {
        _voiceBusy = true;
        try
        {
            while (_voiceQueue.Count > 0)
            {
                var (line, queued) = _voiceQueue.Dequeue();
                if (DateTime.Now - queued > VoiceStale || IsQuiet || !IsVoiceOn) continue;

                var path = Path.Combine(_soundsPath, $"Voice{line}.wav");
                if (!File.Exists(path)) continue;

                var player = new Player();
                await player.Play(path);
                while (player.Playing)
                    await Task.Delay(30);
                await Task.Delay(150);
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
        finally
        {
            _voiceBusy = false;
        }
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
        _voiceQueue.Clear();
        StopAllLoops();
    }
}
