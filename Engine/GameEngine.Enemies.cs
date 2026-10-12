using System;
using System.Linq;
using Avalonia;
using AVASinstar.Models;

namespace AVASinstar.Engine;

/// <summary>The zones, in the order the cabinet cycles through them after the first.</summary>
public enum ZoneKind
{
    First,
    Worker,
    Warrior,
    Planetoid,
    Void
}

// The zones, and the workers and warriors that serve the Sinistar.
public partial class GameEngine
{
    public int Zone { get; private set; }

    public ZoneKind ZoneKind => Zone == 1 ? ZoneKind.First : (ZoneKind)(1 + (Zone - 2) % 4);

    public string ZoneName => ZoneKind switch
    {
        ZoneKind.First => "ZONE 1",
        ZoneKind.Worker => $"ZONE {Zone}   WORKER ZONE",
        ZoneKind.Warrior => $"ZONE {Zone}   WARRIOR ZONE",
        ZoneKind.Planetoid => $"ZONE {Zone}   PLANETOID ZONE",
        _ => $"ZONE {Zone}   VOID ZONE"
    };

    private int Level => Math.Min(Zone - 1, 10);
    private int WorkerTarget => Math.Min(10, 5 + Level / 2) + (ZoneKind == ZoneKind.Worker ? 5 : 0);
    private int WarriorTarget => Math.Min(7, 2 + Level / 2) + (ZoneKind == ZoneKind.Warrior ? 4 : 0);

    /// <summary>Workers can patch up a damaged Sinistar in every zone but the first, as on the cabinet.</summary>
    public bool WorkersRepair => Zone > 1;

    private double _workerSpawnTimer, _warriorSpawnTimer;

    /// <summary>Lays out a new zone round the ship: planetoids, the Sinistar's building site, its workers and warriors.</summary>
    private void BuildZone()
    {
        Planetoids.Clear();
        Crystals.Clear();
        Workers.Clear();
        Warriors.Clear();
        Shots.Clear();
        Bombs.Clear();

        int rocks = ZoneKind switch { ZoneKind.Planetoid => 52, ZoneKind.Void => 14, _ => 34 };
        for (int i = 0; i < rocks; i++)
        {
            Point p;
            int tries = 0;
            do
            {
                p = new Point(Random.NextDouble() * World.Width, Random.NextDouble() * World.Height);
            } while ((World.Distance(p, Ship.Position) < 350 || Planetoids.Any(r => World.Distance(r.Position, p) < 260)) && ++tries < 30);
            Planetoids.Add(new Planetoid(p, 34 + Random.NextDouble() * 40, 3 + Random.Next(6), Random));
        }

        double a = Random.NextDouble() * Math.PI * 2;
        Sinistar = new Sinistar(World.Wrap(Ship.Position + new Vector(Math.Cos(a), Math.Sin(a)) * 2600), Random)
        {
            TauntTimer = 6
        };
        Planetoids.RemoveAll(r => World.Distance(r.Position, Sinistar.Position) < r.Radius + 160);

        for (int i = 0; i < WorkerTarget; i++) SpawnWorker();
        for (int i = 0; i < WarriorTarget; i++) SpawnWarrior();
        _workerSpawnTimer = 6;
        _warriorSpawnTimer = 9;
    }

    private void SpawnWorker()
    {
        double a = Random.NextDouble() * Math.PI * 2;
        var p = World.Wrap(Sinistar.Position + new Vector(Math.Cos(a), Math.Sin(a)) * (160 + Random.NextDouble() * 500));
        Workers.Add(new Worker(p) { Wobble = Random.NextDouble() * 10 });
    }

    private void SpawnWarrior()
    {
        Point p;
        int tries = 0;
        do
        {
            p = new Point(Random.NextDouble() * World.Width, Random.NextDouble() * World.Height);
        } while (World.Distance(p, Ship.Position) < 1100 && ++tries < 30);
        Warriors.Add(new Warrior(p) { FireTimer = 2 + Random.NextDouble() * 2, Strafe = Random.Next(2) * 2 - 1 });
    }

    // Losses are made good over time, from the far side of the sector.
    private void SpawnReinforcements(double deltaTime)
    {
        if (!IsInPlay) return;
        _workerSpawnTimer -= deltaTime;
        if (_workerSpawnTimer <= 0)
        {
            _workerSpawnTimer = 6;
            if (Workers.Count < WorkerTarget) SpawnWorker();
        }
        _warriorSpawnTimer -= deltaTime;
        if (_warriorSpawnTimer <= 0)
        {
            _warriorSpawnTimer = Math.Max(4, 9 - Level * 0.5);
            if (Warriors.Count < WarriorTarget) SpawnWarrior();
        }
    }

    private static void Steer(GameObject o, Vector wanted, double agility, double deltaTime)
    {
        o.Velocity += (wanted - o.Velocity) * Math.Min(1, agility * deltaTime);
        if (o.Velocity.Length > 1)
            o.Rotation = Math.Atan2(o.Velocity.Y, o.Velocity.X);
    }

    private static Vector Toward(Point from, Point to, double speed)
    {
        var d = World.Delta(from, to);
        double length = d.Length;
        return length < 1e-6 ? default : d / length * speed;
    }

    // ----- Workers -----

    // A worker finds a loose crystal (or mines one out of a planetoid) and carries it to the Sinistar.
    private void UpdateWorkers(double deltaTime)
    {
        double speed = Math.Min(330, 230 + 10 * Level);
        bool siteWantsPieces = !Sinistar.IsComplete && (!Sinistar.IsAwake || WorkersRepair);

        foreach (var w in Workers)
        {
            w.Wobble += deltaTime;
            var weave = new Vector(Math.Cos(w.Wobble * 2.3), Math.Sin(w.Wobble * 1.9)) * 40;

            switch (w.Task)
            {
                case WorkerTask.Carrying:
                    if (!siteWantsPieces)
                    {
                        // Nothing to build: idle about near the site, holding on to the crystal.
                        Steer(w, Toward(w.Position, Sinistar.Position, speed * 0.3) + weave, 2, deltaTime);
                        break;
                    }
                    Steer(w, Toward(w.Position, Sinistar.Position, speed) + weave * 0.5, 3, deltaTime);
                    if (World.Distance(w.Position, Sinistar.Position) < Sinistar.Radius * 0.8)
                    {
                        if (Sinistar.AddPiece())
                        {
                            if (Sinistar.IsAwake)
                                Sinistar.BombHits = Math.Max(0, Sinistar.BombHits - 1);
                            if (IsInPlay && World.Distance(Sinistar.Position, Ship.Position) < 1400)
                                Sound.PlayPiece();
                        }
                        w.Task = WorkerTask.Seeking;
                    }
                    break;

                case WorkerTask.Mining:
                    if (w.Rock == null || !Planetoids.Contains(w.Rock) || w.Rock.Crystals <= 0)
                    {
                        w.Task = WorkerTask.Seeking;
                        break;
                    }
                    var spot = World.Wrap(w.Rock.Position + new Vector(Math.Cos(w.Wobble), Math.Sin(w.Wobble)) * (w.Rock.Radius + 14));
                    Steer(w, Toward(w.Position, spot, speed * 0.5), 4, deltaTime);
                    w.TaskTime -= deltaTime;
                    if (w.TaskTime <= 0)
                    {
                        w.Rock.Crystals--;
                        w.Task = WorkerTask.Carrying;
                    }
                    break;

                default:
                    if (w.Target is { IsAlive: true } crystal)
                    {
                        Steer(w, Toward(w.Position, crystal.Position, speed), 4, deltaTime);
                        if (World.Distance(w.Position, crystal.Position) < w.Radius + crystal.Radius + 4)
                        {
                            crystal.IsAlive = false;
                            w.Target = null;
                            w.Task = WorkerTask.Carrying;
                        }
                        break;
                    }
                    w.Target = null;

                    var loose = Crystals.Where(c => c.IsAlive && !c.IsClaimed && World.Distance(c.Position, w.Position) < 1600)
                        .OrderBy(c => World.Distance(c.Position, w.Position)).FirstOrDefault();
                    if (loose != null)
                    {
                        loose.IsClaimed = true;
                        w.Target = loose;
                        break;
                    }

                    var rock = Planetoids.Where(r => r.Crystals > 0)
                        .OrderBy(r => World.Distance(r.Position, w.Position)).FirstOrDefault();
                    if (rock == null)
                    {
                        Steer(w, weave * 3, 1, deltaTime);
                        break;
                    }
                    Steer(w, Toward(w.Position, rock.Position, speed) + weave * 0.5, 3, deltaTime);
                    if (World.Distance(w.Position, rock.Position) < rock.Radius + 40)
                    {
                        w.Rock = rock;
                        w.Task = WorkerTask.Mining;
                        w.TaskTime = 4;
                    }
                    break;
            }
            w.Update(deltaTime);
        }
    }

    private void KillWorker(Worker w)
    {
        w.IsAlive = false;
        AddScore(Worker.Points);
        Explosions.Add(Explosion.CreateSmall(w.Position, WorkerColor));
        Sound.PlayWorkerBoom();
        if (w.Target != null) w.Target.IsClaimed = false;
        // A worker shot down drops what it was carrying.
        if (w.HasCrystal)
            Crystals.Add(new Crystal(w.Position, w.Velocity * 0.3));
    }

    // ----- Warriors -----

    // A warrior near the player attacks it; otherwise it shoots planetoids to free crystals for the workers.
    private void UpdateWarriors(double deltaTime)
    {
        double speed = Math.Min(420, 300 + 12 * Level);
        double fireInterval = Math.Max(0.7, 2.0 - 0.12 * Level);

        foreach (var w in Warriors)
        {
            w.FireTimer -= deltaTime;
            var toShip = World.Delta(w.Position, Ship.Position);
            double distance = toShip.Length;

            if (ShipAlive && IsInPlay && distance < 1000)
            {
                // Close to a stand-off range and circle there, firing with a little lead.
                var dir = toShip / Math.Max(distance, 1);
                var side = new Vector(-dir.Y, dir.X) * w.Strafe;
                var wanted = distance > 340 ? dir * speed + side * speed * 0.3 : side * speed * 0.8 - dir * speed * 0.2;
                Steer(w, wanted, 2.5, deltaTime);
                if (Random.NextDouble() < deltaTime * 0.3) w.Strafe = -w.Strafe;

                if (w.FireTimer <= 0 && distance < 750)
                {
                    w.FireTimer = fireInterval * (0.7 + Random.NextDouble() * 0.6);
                    double t = distance / 620;
                    var aim = toShip + Ship.Velocity * t * 0.7;
                    var shotDir = aim / Math.Max(aim.Length, 1);
                    Shots.Add(new Shot(World.Wrap(w.Position + shotDir * 16), shotDir * 620, 1.4, fromPlayer: false));
                    if (State == GameState.Playing) Sound.PlayWarriorFire();
                }
            }
            else
            {
                if (w.Rock == null || !Planetoids.Contains(w.Rock) || w.Rock.Crystals <= 0)
                    w.Rock = Planetoids.Where(r => r.Crystals > 0).OrderBy(r => World.Distance(r.Position, w.Position)).FirstOrDefault();
                if (w.Rock != null)
                {
                    var toRock = World.Delta(w.Position, w.Rock.Position);
                    double d = toRock.Length;
                    var dir = toRock / Math.Max(d, 1);
                    Steer(w, d > 240 ? dir * speed * 0.7 : new Vector(-dir.Y, dir.X) * speed * 0.3, 2, deltaTime);
                    if (w.FireTimer <= 0 && d < 320)
                    {
                        w.FireTimer = 1.5 + Random.NextDouble();
                        Shots.Add(new Shot(World.Wrap(w.Position + dir * 16), dir * 620, 1.0, fromPlayer: false));
                    }
                }
            }
            w.Update(deltaTime);
        }
    }

    private void KillWarrior(Warrior w)
    {
        w.IsAlive = false;
        AddScore(Warrior.Points);
        Explosions.Add(Explosion.CreateShip(w.Position, WarriorColor));
        Sound.PlayWarriorBoom();
    }
}
