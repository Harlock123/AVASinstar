using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVASinstar.Models;

namespace AVASinstar.Engine;

public enum GameState
{
    StartScreen,
    Playing,
    Paused,
    GameOver,
    Attract,
    EnterInitials,
    HighScores
}

/// <summary>The stick and buttons, from the player or the demo pilot.</summary>
public readonly record struct ShipControls(Vector Stick, bool Fire, bool Bomb);

/// <summary>
/// The game: the player's ship, the planetoids and their crystals, the workers and warriors, the
/// Sinistar and the Sinibombs (in GameEngine.Enemies and .Sinistar), scoring and lives, and the title,
/// attract and high-score cycle around it.
/// </summary>
public partial class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    public const int StartingLives = 3;
    public const int MaxLives = 9;
    public const int ExtraShipEvery = 25000;
    public const int CrystalPoints = 200;
    public const int RockHitPoints = 5;
    private const double ShotSpeed = 1050;
    private const double ShotLife = 0.6;
    private const double RespawnDelay = 2.5;

    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 75.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns
    private const double GameOverBeforeEntry = 2.5; // "GAME OVER" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

    public static readonly Color ShipColor = Color.FromRgb(230, 235, 245);
    public static readonly Color RockColor = Color.FromRgb(150, 128, 104);
    public static readonly Color CrystalColor = Color.FromRgb(235, 245, 255);
    public static readonly Color WorkerColor = Color.FromRgb(255, 70, 60);
    public static readonly Color WarriorColor = Color.FromRgb(70, 200, 255);
    public static readonly Color SinistarColor = Color.FromRgb(170, 170, 178);
    public static readonly Color BombColor = Color.FromRgb(255, 120, 200);

    public GameState State { get; private set; }
    public PlayerShip Ship { get; private set; } = new(World.Center);
    public bool ShipAlive { get; private set; }
    public int Score { get; private set; }
    public int Lives { get; private set; }

    public List<Planetoid> Planetoids { get; } = new();
    public List<Crystal> Crystals { get; } = new();
    public List<Worker> Workers { get; } = new();
    public List<Warrior> Warriors { get; } = new();
    public List<Shot> Shots { get; } = new();
    public List<Sinibomb> Bombs { get; } = new();
    public List<Explosion> Explosions { get; } = new();

    public string? Banner { get; private set; }
    public string? SubBanner { get; private set; }
    public double BannerTime { get; private set; }
    public double Shake { get; private set; }
    public double Clock { get; private set; }
    public bool IsInPlay => State is GameState.Playing or GameState.Attract;

    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;
    public InitialsEntry? Entry { get; private set; }
    public int HighlightIndex { get; private set; } = -1;
    public bool IsEntryPending => _entryPending;

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private ShipControls _controls;
    private bool _wasBombing;
    private double _respawnTimer;
    private int _nextExtraShip;

    private bool _wasStartPressed;
    private double _gameOverTimer;
    private bool _entryPending;
    private double _entryClock;
    private double _entryIdle;
    private double _padRepeat;
    private bool _wasPadUp, _wasPadDown, _wasPadLeft, _wasPadRight, _wasPadAccept, _wasPadDone, _wasLeftMouse;
    private double _tableTimer;
    private bool _tableLeadsToDemo;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private readonly AttractPilot _pilot = new();

    public GameEngine()
    {
        State = GameState.StartScreen;
        Zone = 1;
        BuildZone();
    }

    public void Dispose()
    {
        // Closing the window mid-entry still keeps the score, under whatever initials are showing.
        if (_entryPending)
            CommitEntry();

        Sound.Dispose();
        Input.Dispose();
    }

    // ----- Starting, stopping and the attract cycle -----

    public void StartNewGame()
    {
        BeginRun(GameState.Playing);
        Sound.PlayZone();
    }

    /// <summary>The game plays itself, silently, until its ships are gone or time runs out.</summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = false;
        Zone = 1;
        BuildZone();
        ShipAlive = false;
        _idleTimer = 0;
        HighlightIndex = -1;
        Banner = SubBanner = null;
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = state == GameState.Attract;
        Score = 0;
        Lives = StartingLives;
        _nextExtraShip = ExtraShipEvery;
        Zone = 1;
        Ship = new PlayerShip(World.Center);
        ShipAlive = true;
        Ship.Invulnerable = 2;
        _wasBombing = true;
        _pilot.Reset();
        BuildZone();
        ShowBanner(ZoneName, "COLLECT CRYSTALS FOR SINIBOMBS", 3);
    }

    private void ShowHighScores(bool leadsToDemo)
    {
        State = GameState.HighScores;
        _tableTimer = HighScoresShowTime;
        _tableLeadsToDemo = leadsToDemo;
        if (leadsToDemo)
            HighlightIndex = -1;
    }

    private void BeginInitials()
    {
        State = GameState.EnterInitials;
        Entry = new InitialsEntry(Table.LastInitials);
        _entryClock = 0;
        _entryIdle = 0;
        _wasPadUp = _wasPadDown = _wasPadLeft = _wasPadRight = _wasPadAccept = _wasPadDone = true;
        _wasLeftMouse = true;
    }

    private void CommitEntry()
    {
        _entryPending = false;
        string initials = Entry?.Initials ?? Table.LastInitials;
        HighlightIndex = Table.Insert(initials, Score, Zone);
        Table.Save();
        Entry = null;
    }

    private bool EntryAcceptsInput => State == GameState.EnterInitials && _entryClock >= EntryInputGrace && Entry != null;

    /// <summary>Keyboard input during initials entry, passed straight from the window.</summary>
    public void InitialsKey(Avalonia.Input.Key key)
    {
        if (!EntryAcceptsInput) return;
        var entry = Entry!;
        _entryIdle = 0;

        if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
            entry.Type((char)('A' + (key - Avalonia.Input.Key.A)));
        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.D0)));
        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.NumPad0)));
        else if (key is Avalonia.Input.Key.OemPeriod or Avalonia.Input.Key.Decimal)
            entry.Type('.');
        else if (key == Avalonia.Input.Key.Up)
            entry.Cycle(1);
        else if (key == Avalonia.Input.Key.Down)
            entry.Cycle(-1);
        else if (key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Back)
            entry.Back();
        else if (key == Avalonia.Input.Key.Right)
            entry.Move(1);
        else if (key == Avalonia.Input.Key.Space)
            entry.Advance();
        else if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
            entry.Finish();
    }

    // Gamepad and mouse input during initials entry: up/down dial the letter (holding repeats),
    // left/right move, a fire button accepts a letter, Start finishes. The wheel dials, a click accepts.
    private void UpdateInitials(double deltaTime)
    {
        _entryClock += deltaTime;
        _entryIdle += deltaTime;
        var entry = Entry!;

        bool up = Input.PadUp, down = Input.PadDown, left = Input.PadLeft, right = Input.PadRight;
        bool accept = Input.PadAccept, done = Input.PadDone, click = Input.IsLeftMousePressed;

        if (EntryAcceptsInput)
        {
            if (up || down || left || right || accept || done || click || Input.MouseWheelDelta != 0)
                _entryIdle = 0;

            int dial = 0;
            if ((up && !_wasPadUp) || (down && !_wasPadDown))
            {
                dial = up ? 1 : -1;
                _padRepeat = 0.4;
            }
            else if (up || down)
            {
                _padRepeat -= deltaTime;
                if (_padRepeat <= 0)
                {
                    dial = up ? 1 : -1;
                    _padRepeat = 0.12;
                }
            }
            if (Input.MouseWheelDelta != 0)
                dial = Math.Sign(Input.MouseWheelDelta);
            if (dial != 0)
                entry.Cycle(dial);

            if (left && !_wasPadLeft) entry.Back();
            if (right && !_wasPadRight) entry.Move(1);
            if ((accept && !_wasPadAccept) || (click && !_wasLeftMouse)) entry.Advance();
            if (done && !_wasPadDone) entry.Finish();
        }

        _wasPadUp = up; _wasPadDown = down; _wasPadLeft = left; _wasPadRight = right;
        _wasPadAccept = accept; _wasPadDone = done; _wasLeftMouse = click;

        if (_entryIdle >= EntryTimeout)
            entry.Finish();

        if (entry.IsDone)
        {
            CommitEntry();
            ShowHighScores(leadsToDemo: false);
        }
    }

    // ----- The frame -----

    public void Update(double deltaTime)
    {
        Clock += deltaTime;
        bool start = Input.IsFiring;
        bool startPressed = start && !_wasStartPressed;
        _wasStartPressed = start;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                return;

            case GameState.StartScreen:
            case GameState.HighScores:
                // The sector drifts on behind the title, with the workers about their business.
                IdleWorld(deltaTime);
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (State == GameState.StartScreen)
                {
                    _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                    if (_idleTimer >= AttractIdleDelay)
                        ShowHighScores(leadsToDemo: true);
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _tableTimer -= deltaTime;
                if (_tableTimer <= 0)
                {
                    if (_tableLeadsToDemo)
                        StartAttract();
                    else
                        ReturnToTitle();
                }
                return;

            case GameState.EnterInitials:
                UpdateExplosions(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                UpdateWorld(deltaTime);
                _gameOverTimer -= deltaTime;
                if (_entryPending)
                {
                    if (_gameOverTimer <= GameOverInputDelay - GameOverBeforeEntry)
                        BeginInitials();
                    return;
                }
                if (startPressed && _gameOverTimer <= 0)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= GameOverIdleDelay)
                    ReturnToTitle();
                return;

            case GameState.Attract:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _attractTimer -= deltaTime;
                if (_attractTimer <= 0)
                {
                    ReturnToTitle();
                    return;
                }
                _controls = _pilot.Fly(this, deltaTime);
                break;

            default:
                _controls = new ShipControls(Input.Stick, Input.IsFiring, Input.IsBombing);
                break;
        }

        UpdateShip(deltaTime);
        UpdateWorld(deltaTime);
    }

    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            Sound.StopAllLoops();
        }
    }

    public void Resume()
    {
        if (State == GameState.Paused)
        {
            State = GameState.Playing;
            _wasBombing = true;
        }
    }

    private void ShowBanner(string text, string? sub, double time)
    {
        Banner = text;
        SubBanner = sub;
        BannerTime = time;
    }

    /// <summary>Everything but the player: rocks, crystals, workers, warriors, the Sinistar, shots and bombs.</summary>
    private void UpdateWorld(double deltaTime)
    {
        BannerTime = Math.Max(0, BannerTime - deltaTime);
        Shake = Math.Max(0, Shake - deltaTime);

        foreach (var rock in Planetoids) rock.Update(deltaTime);
        foreach (var crystal in Crystals) crystal.Update(deltaTime);
        UpdateWorkers(deltaTime);
        UpdateWarriors(deltaTime);
        UpdateSinistar(deltaTime);
        UpdateShots(deltaTime);
        UpdateBombs(deltaTime);
        UpdateExplosions(deltaTime);
        CheckPickups();
        SpawnReinforcements(deltaTime);
        RegrowCrystals(deltaTime);

        Crystals.RemoveAll(c => !c.IsAlive);
        Workers.RemoveAll(w => !w.IsAlive);
        Warriors.RemoveAll(w => !w.IsAlive);
        Shots.RemoveAll(s => !s.IsAlive);
        Bombs.RemoveAll(b => !b.IsAlive);
    }

    // On the title screens the ship sits idle in the middle and the sector carries on round it.
    private void IdleWorld(double deltaTime)
    {
        BannerTime = 0;
        foreach (var rock in Planetoids) rock.Update(deltaTime);
        UpdateWorkers(deltaTime);
        UpdateExplosions(deltaTime);
        Crystals.RemoveAll(c => !c.IsAlive);
    }

    private void UpdateExplosions(double deltaTime)
    {
        foreach (var e in Explosions) e.Update(deltaTime);
        Explosions.RemoveAll(e => e.IsFinished);
    }

    // ----- The player's ship -----

    private void UpdateShip(double deltaTime)
    {
        if (!ShipAlive)
        {
            _respawnTimer -= deltaTime;
            if (_respawnTimer <= 0 && Lives > 0 && State != GameState.GameOver)
            {
                ShipAlive = true;
                Ship.IsAlive = true;
                Ship.Velocity = default;
                Ship.Invulnerable = 3;
                _wasBombing = true;
            }
            return;
        }

        Ship.Fly(_controls.Stick, deltaTime);
        if (Ship.IsThrusting) Sound.StartThrust();
        else Sound.StopThrust();

        // Bouncing off planetoids.
        foreach (var rock in Planetoids)
        {
            var away = World.Delta(rock.Position, Ship.Position);
            double d = away.Length, overlap = rock.Radius * 0.85 + Ship.Radius - d;
            if (overlap > 0 && d > 0.01)
            {
                var n = away / d;
                Ship.Position = World.Wrap(Ship.Position + n * overlap);
                double vn = Ship.Velocity.X * n.X + Ship.Velocity.Y * n.Y;
                if (vn < 0) Ship.Velocity -= n * vn * 1.6;
            }
        }

        if (_controls.Fire && Ship.FireTimer <= 0)
        {
            Ship.FireTimer = PlayerShip.FireInterval;
            Shots.Add(new Shot(Ship.Nose, Ship.Heading * ShotSpeed + Ship.Velocity * 0.5, ShotLife, fromPlayer: true));
            Sound.PlayFire();
        }

        bool bombPressed = _controls.Bomb && !_wasBombing;
        _wasBombing = _controls.Bomb;
        if (bombPressed)
            LaunchBomb();
    }

    private void DestroyShip(bool eaten)
    {
        if (!ShipAlive || Ship.Invulnerable > 0) return;
        ShipAlive = false;
        Ship.IsAlive = false;
        Explosions.Add(Explosion.CreateShip(Ship.Position, ShipColor));
        Explosions.Add(Explosion.CreateSmall(Ship.Position, Color.FromRgb(255, 200, 120)));
        Shake = 0.6;
        Sound.StopThrust();
        Sound.PlayShipBoom();
        if (eaten)
        {
            Sound.PlayRoar();
            Sound.Say(Voice.IHunger);
        }
        foreach (var shot in Shots) shot.IsAlive = false;
        SinistarRetreats();
        Lives--;

        if (Lives > 0)
        {
            _respawnTimer = RespawnDelay;
            return;
        }

        // A demo that runs out of ships lingers on the explosion, then hands back to the title.
        if (State == GameState.Attract)
        {
            _attractTimer = Math.Min(_attractTimer, 3);
            return;
        }

        State = GameState.GameOver;
        Sound.StopAllLoops();
        Banner = SubBanner = null;
        _gameOverTimer = GameOverInputDelay;
        _idleTimer = 0;
        _entryPending = Table.Qualifies(Score);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtraShip)
        {
            _nextExtraShip += ExtraShipEvery;
            if (Lives < MaxLives)
            {
                Lives++;
                Sound.PlayExtraShip();
            }
        }
    }

    // ----- Shots and pickups -----

    private void UpdateShots(double deltaTime)
    {
        foreach (var shot in Shots)
        {
            shot.Update(deltaTime);
            if (!shot.IsAlive) continue;

            var rock = Planetoids.FirstOrDefault(r => World.Distance(r.Position, shot.Position) < r.Radius * 0.85);
            if (rock != null)
            {
                shot.IsAlive = false;
                HitRock(rock, shot.FromPlayer, shot.Velocity);
                continue;
            }

            if (shot.FromPlayer)
            {
                foreach (var w in Workers)
                {
                    if (w.IsAlive && shot.CollidesWith(w))
                    {
                        shot.IsAlive = false;
                        KillWorker(w);
                        break;
                    }
                }
                if (!shot.IsAlive) continue;
                foreach (var w in Warriors)
                {
                    if (w.IsAlive && shot.CollidesWith(w))
                    {
                        shot.IsAlive = false;
                        KillWarrior(w);
                        break;
                    }
                }
                // Lasers glance off the Sinistar: only Sinibombs can hurt it.
                if (shot.IsAlive && Sinistar.PiecesBuilt > 0 && World.Distance(Sinistar.Position, shot.Position) < Sinistar.Radius * 0.9)
                    shot.IsAlive = false;
            }
            else if (ShipAlive && shot.CollidesWith(Ship))
            {
                shot.IsAlive = false;
                DestroyShip(eaten: false);
            }
        }
    }

    /// <summary>A shot strikes a planetoid; now and then it knocks a crystal loose.</summary>
    private void HitRock(Planetoid rock, bool byPlayer, Vector shotVelocity)
    {
        rock.HitFlash = 0.08;
        if (byPlayer)
        {
            AddScore(RockHitPoints);
            Sound.PlayRockHit();
        }
        if (rock.Crystals > 0 && Random.NextDouble() < (byPlayer ? 0.07 : 0.3))
        {
            rock.Crystals--;
            var outward = World.Delta(rock.Position, World.Wrap(rock.Position - shotVelocity / Math.Max(shotVelocity.Length, 1) * rock.Radius));
            var dir = outward / Math.Max(outward.Length, 1);
            double a = Math.Atan2(dir.Y, dir.X) + (Random.NextDouble() - 0.5) * 1.2;
            var spawn = World.Wrap(rock.Position + new Vector(Math.Cos(a), Math.Sin(a)) * (rock.Radius + 8));
            Crystals.Add(new Crystal(spawn, new Vector(Math.Cos(a), Math.Sin(a)) * (90 + Random.NextDouble() * 80)));
            if (byPlayer) Sound.PlayCrystal();
        }
    }

    // Planetoids slowly grow new crystals, so a sector is never mined out for good.
    private double _regrowTimer = 4;

    private void RegrowCrystals(double deltaTime)
    {
        _regrowTimer -= deltaTime;
        if (_regrowTimer > 0 || Planetoids.Count == 0) return;
        _regrowTimer = 4;
        var rock = Planetoids[Random.Next(Planetoids.Count)];
        if (rock.Crystals < rock.MaxCrystals)
            rock.Crystals++;
    }

    private void CheckPickups()
    {
        if (!ShipAlive) return;
        foreach (var crystal in Crystals)
        {
            if (crystal.IsAlive && World.Distance(crystal.Position, Ship.Position) < Ship.Radius + crystal.Radius + 6)
            {
                crystal.IsAlive = false;
                if (Ship.Bombs < PlayerShip.MaxBombs)
                    Ship.Bombs++;
                AddScore(CrystalPoints);
                Sound.PlayPickup();
            }
        }
    }
}
