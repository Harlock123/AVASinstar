using System;
using System.Linq;
using Avalonia;
using AVASinstar.Models;

namespace AVASinstar.Engine;

/// <summary>
/// Flies the ship in attract mode. It mines the nearest planetoid for crystals and scoops them up,
/// fights off warriors that come close, bombs the Sinistar while it's being built once it has a few
/// Sinibombs, and runs from it, bombing as it goes, once it lives.
/// </summary>
public class AttractPilot
{
    private static readonly Random Random = new();
    private double _hesitation;
    private bool _bombToggle;

    public void Reset() => _hesitation = 0;

    public ShipControls Fly(GameEngine engine, double deltaTime)
    {
        if (!engine.ShipAlive)
            return default;

        var ship = engine.Ship;
        _hesitation -= deltaTime;
        if (_hesitation < -6 && Random.NextDouble() < deltaTime * 0.2)
            _hesitation = 0.5 + Random.NextDouble() * 0.6;
        if (_hesitation > 0)
            return new ShipControls(new Vector(Math.Cos(ship.Rotation), Math.Sin(ship.Rotation)) * 0.5, false, false);

        var sinistar = engine.Sinistar;
        _bombToggle = !_bombToggle;

        // The Sinistar lives: run, and drop bombs on it as it follows. With no bombs left, keep
        // mining for more while it's still a safe way off.
        double sinistarDistance = World.Distance(sinistar.Position, ship.Position);
        if (sinistar.IsAwake && (ship.Bombs > 0 || sinistarDistance < 700))
        {
            var away = World.Delta(sinistar.Position, ship.Position);
            double d = away.Length;
            var flee = away / Math.Max(d, 1);
            // Too far off for the bombs to reach in good time: close in first.
            if (ship.Bombs > 0 && d > 1000)
                return new ShipControls(-flee, false, d < 1400 && _bombToggle);
            // Swing round in a wide circle, so the bombs only have a short way to go.
            var side = new Vector(-flee.Y, flee.X);
            var stick = d < 500 ? flee : flee * 0.6 + side * 0.8;
            return new ShipControls(Norm(stick), d < 900, ship.Bombs > 0 && d < 1400 && _bombToggle);
        }

        // A warrior close by: turn and fight it.
        var warrior = engine.Warriors.OrderBy(w => World.Distance(w.Position, ship.Position)).FirstOrDefault();
        if (warrior != null && World.Distance(warrior.Position, ship.Position) < 500)
            return Attack(ship, warrior.Position, warrior.Velocity, 0.6);

        // A loose crystal: go and get it.
        var crystal = engine.Crystals.Where(c => !c.IsClaimed || World.Distance(c.Position, ship.Position) < 400)
            .OrderBy(c => World.Distance(c.Position, ship.Position)).FirstOrDefault();
        if (crystal != null && World.Distance(crystal.Position, ship.Position) < 900)
            return new ShipControls(Norm(World.Delta(ship.Position, crystal.Position)), false, false);

        // Workers are fair game too.
        var worker = engine.Workers.OrderBy(w => World.Distance(w.Position, ship.Position)).FirstOrDefault();
        if (worker != null && World.Distance(worker.Position, ship.Position) < 450)
            return Attack(ship, worker.Position, worker.Velocity, 0.5);

        // Otherwise: mine the nearest planetoid that still has crystals.
        var rock = engine.Planetoids.Where(r => r.Crystals > 0).OrderBy(r => World.Distance(r.Position, ship.Position)).FirstOrDefault();
        if (rock == null)
            return new ShipControls(new Vector(Math.Cos(ship.Rotation), Math.Sin(ship.Rotation)) * 0.6, false, false);
        var toRock = World.Delta(ship.Position, rock.Position);
        double dist = toRock.Length;
        if (dist > rock.Radius + 260)
            return new ShipControls(Norm(toRock), false, false);
        // In range: face it, hold off, and fire.
        var face = Norm(toRock) * (dist < rock.Radius + 120 ? 0.06 : 0.25);
        bool aimed = Math.Abs(World.AngleDiff(Math.Atan2(toRock.Y, toRock.X), ship.Rotation)) < 0.15;
        return new ShipControls(face, aimed, false);
    }

    private static ShipControls Attack(PlayerShip ship, Point target, Vector velocity, double approach)
    {
        var to = World.Delta(ship.Position, target) + velocity * 0.25;
        bool aimed = Math.Abs(World.AngleDiff(Math.Atan2(to.Y, to.X), ship.Rotation)) < 0.12;
        return new ShipControls(Norm(to) * approach, aimed, false);
    }

    private static Vector Norm(Vector v)
    {
        double length = v.Length;
        return length < 1e-6 ? default : v / length;
    }
}
