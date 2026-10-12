using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVASinstar.Models;

namespace AVASinstar.Engine;

// The Sinistar, the Sinibombs that destroy it, and the move to the next zone.
public partial class GameEngine
{
    public Sinistar Sinistar { get; private set; } = null!;

    /// <summary>Counting down after the Sinistar is destroyed, before the next zone.</summary>
    public double ZoneTimer { get; private set; }

    private static readonly Voice[] Taunts =
        { Voice.IHunger, Voice.RunCoward, Voice.IAmSinistar, Voice.BewareCoward, Voice.RunRunRun, Voice.IHungerCoward };

    private void UpdateSinistar(double deltaTime)
    {
        var s = Sinistar;
        s.HitFlash = Math.Max(0, s.HitFlash - deltaTime);

        if (ZoneTimer > 0)
        {
            ZoneTimer -= deltaTime;
            if (ZoneTimer <= 0 && IsInPlay)
                NextZone();
            return;
        }

        if (s.PiecesBuilt == 0 && !s.IsAwake) return;

        // The last piece in place: it wakes, and the hunt begins.
        if (!s.IsAwake && s.IsComplete)
        {
            s.IsAwake = true;
            s.TauntTimer = 4;
            Sound.PlayRoar();
            Sound.Say(Voice.BewareILive);
            ShowBanner("BEWARE, I LIVE!", null, 2.5);
        }

        s.Mouth = s.IsAwake ? 0.5 + 0.5 * Math.Sin(Clock * 7) : 0;
        if (!s.IsAwake)
        {
            s.Velocity = default;
            return;
        }

        if (State == GameState.Playing)
            Sound.StartHeartbeat();

        // It comes for the ship: fast when far off, slower close in, so a ship at full speed can just stay ahead.
        if (ShipAlive && IsInPlay)
        {
            var to = World.Delta(s.Position, Ship.Position);
            double d = to.Length;
            double speed = d > 1100 ? 480 : Math.Min(430, 300 + 14 * Level);
            var lead = to + Ship.Velocity * Math.Min(1.0, d / 600);
            Steer(s, lead / Math.Max(lead.Length, 1) * speed, 1.3, deltaTime);

            if (d < s.Radius * 0.75 + Ship.Radius)
                DestroyShip(eaten: true);

            s.TauntTimer -= deltaTime;
            if (s.TauntTimer <= 0)
            {
                s.TauntTimer = 5 + Random.NextDouble() * 4;
                Sound.Say(Taunts[Random.Next(Taunts.Length)]);
            }
        }
        else
        {
            s.Velocity *= Math.Max(0, 1 - deltaTime);
        }
        s.Position = World.Wrap(s.Position + s.Velocity * deltaTime);
    }

    // After the Sinistar catches the ship, it drifts off a way, so the next ship has room.
    private void SinistarRetreats()
    {
        if (!Sinistar.IsAwake) return;
        var away = World.Delta(Ship.Position, Sinistar.Position);
        var dir = away.Length > 1 ? away / away.Length : new Vector(1, 0);
        Sinistar.Position = World.Wrap(Ship.Position + dir * 1600);
        Sinistar.Velocity = default;
    }

    // ----- Sinibombs -----

    private void LaunchBomb()
    {
        if (Ship.Bombs <= 0)
        {
            Sound.PlayEmpty();
            return;
        }
        Ship.Bombs--;
        Bombs.Add(new Sinibomb(Ship.Nose, Ship.Rotation, Ship.Velocity));
        Sound.PlayBombLaunch();
    }

    // A Sinibomb homes on the Sinistar wherever it is. With no Sinistar to find, it fizzles out.
    private void UpdateBombs(double deltaTime)
    {
        bool target = Sinistar.PiecesBuilt > 0 && ZoneTimer <= 0;
        foreach (var bomb in Bombs)
        {
            bomb.Age += deltaTime;
            if (target)
            {
                var to = World.Delta(bomb.Position, Sinistar.Position);
                double wanted = Math.Atan2(to.Y, to.X);
                bomb.Rotation = World.TurnToward(bomb.Rotation, wanted, 4.5 * deltaTime);
                double speed = Math.Min(Sinibomb.Speed + 200, bomb.Velocity.Length + 900 * deltaTime);
                bomb.Velocity = bomb.Heading * speed;
            }

            bomb.Trail.Insert(0, bomb.Position);
            if (bomb.Trail.Count > 10) bomb.Trail.RemoveAt(bomb.Trail.Count - 1);
            bomb.Update(deltaTime);

            if (target && World.Distance(bomb.Position, Sinistar.Position) < Sinistar.Radius * 0.85)
            {
                bomb.IsAlive = false;
                BombHitsSinistar(bomb.Position);
            }
            else if (bomb.Age > (target ? 9 : 1.8))
            {
                bomb.IsAlive = false;
                Explosions.Add(Explosion.CreateSmall(bomb.Position, BombColor));
            }
        }
    }

    private void BombHitsSinistar(Point at)
    {
        var s = Sinistar;
        s.HitFlash = 0.15;
        Explosions.Add(Explosion.CreateShip(at, BombColor));
        Sound.PlayBombHit();
        Shake = Math.Max(Shake, 0.25);

        if (s.IsAwake)
        {
            // Each hit knocks a piece off; thirteen hits finish it, unless the workers patch it up.
            s.BombHits++;
            if (s.RemovePieces(1) > 0)
                AddScore(Sinistar.PiecePoints);
            if (s.BombHits >= Sinistar.BombsToKill || s.PiecesBuilt == 0)
                DestroySinistar();
        }
        else
        {
            // Still being built: each bomb knocks two pieces off.
            AddScore(s.RemovePieces(2) * Sinistar.PiecePoints);
        }
    }

    private void DestroySinistar()
    {
        var s = Sinistar;
        AddScore(Sinistar.Points);
        foreach (var piece in Sinistar.PieceShapes)
        {
            var c = new Point(piece.Average(p => p.X), piece.Average(p => p.Y));
            Explosions.Add(Explosion.CreateShip(World.Wrap(s.Position + new Vector(c.X, c.Y)), SinistarColor));
        }
        Explosions.Add(Explosion.CreateBig(s.Position, Color.FromRgb(255, 80, 60)));
        Explosions.Add(Explosion.CreateBig(s.Position, Colors.White));
        Array.Clear(s.Built);
        s.IsAwake = false;
        s.BombHits = 0;
        Shake = 1.0;
        Sound.StopHeartbeat();
        Sound.ClearVoice();
        Sound.Say(Voice.Scream);
        Sound.PlaySinistarBoom();
        ShowBanner("SINISTAR DESTROYED", $"{Sinistar.Points} POINTS", 3.5);
        ZoneTimer = 4.5;

        // Its servants scatter.
        foreach (var w in Workers) Explosions.Add(Explosion.CreateSmall(w.Position, WorkerColor));
        Workers.Clear();
    }

    private void NextZone()
    {
        Zone++;
        BuildZone();
        Ship.Invulnerable = 2;
        ShowBanner(ZoneName, null, 3);
        Sound.PlayZone();
    }
}
