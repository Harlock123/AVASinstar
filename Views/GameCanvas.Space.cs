using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVASinstar.Engine;
using AVASinstar.Models;

namespace AVASinstar.Views;

// Space round the ship: the stars, the planetoids and crystals, workers, warriors, shots, bombs, the
// Sinistar, and the ship itself.
public partial class GameCanvas
{
    // Three layers of stars that drift past at different speeds as the ship moves.
    private readonly (Point Position, double Depth, double Brightness)[] _stars = MakeStars();

    private static (Point, double, double)[] MakeStars()
    {
        var random = new Random(1983);
        return Enumerable.Range(0, 260)
            .Select(i => (new Point(random.NextDouble() * 1024, random.NextDouble() * 768), 0.15 + (i % 3) * 0.2, 0.3 + random.NextDouble() * 0.7))
            .ToArray();
    }

    private Point _cameraCentre;

    /// <summary>Where a point in space lands on the screen.</summary>
    private Point ToScreen(Point world) => ScreenCentre + World.Delta(_cameraCentre, world);

    private bool OnScreen(Point screen, double margin) =>
        screen.X > -margin && screen.X < ScreenWidth + margin && screen.Y > -margin && screen.Y < ScreenHeight + margin;

    private void DrawSpace(DrawingContext context)
    {
        _cameraCentre = _engine.Ship.Position;
        if (_engine.Shake > 0)
        {
            double amount = Math.Min(1, _engine.Shake * 2.5) * 10;
            _cameraCentre += new Vector((_random.NextDouble() - 0.5) * amount, (_random.NextDouble() - 0.5) * amount);
        }

        DrawStars(context);
        foreach (var rock in _engine.Planetoids) DrawPlanetoid(context, rock);
        DrawSinistar(context);
        foreach (var crystal in _engine.Crystals) DrawCrystal(context, ToScreen(crystal.Position), crystal.Age);
        foreach (var w in _engine.Workers) DrawWorker(context, w);
        foreach (var w in _engine.Warriors) DrawWarrior(context, w);
        foreach (var shot in _engine.Shots) DrawShot(context, shot);
        foreach (var bomb in _engine.Bombs) DrawBomb(context, bomb);
        if (_engine.ShipAlive && _engine.State != GameState.StartScreen && _engine.State != GameState.HighScores)
            DrawShip(context);
        foreach (var e in _engine.Explosions)
        {
            double alpha = e.GetOpacity();
            foreach (var (a, b) in e.GetParticleLines())
                DrawGlowLine(context, ToScreen(a), ToScreen(b), e.Color, alpha, 2);
        }
    }

    private void DrawStars(DrawingContext context)
    {
        var c = _cameraCentre;
        foreach (var (p, depth, brightness) in _stars)
        {
            double x = (p.X - c.X * depth) % ScreenWidth, y = (p.Y - c.Y * depth) % ScreenHeight;
            if (x < 0) x += ScreenWidth;
            if (y < 0) y += ScreenHeight;
            double size = 0.8 + depth * 2;
            context.FillRectangle(GetBrush(Color.FromRgb(200, 210, 255), brightness), new Rect(x, y, size, size));
        }
    }

    private void DrawPlanetoid(DrawingContext context, Planetoid rock)
    {
        var at = ToScreen(rock.Position);
        if (!OnScreen(at, rock.Radius + 10)) return;
        var outline = Place(rock.Outline, at, rock.Spin);
        var fill = rock.HitFlash > 0 ? Color.FromRgb(120, 105, 90) : Color.FromRgb(58, 48, 40);
        DrawShape(context, outline, fill, 1, GameEngine.RockColor, 0.9, 1.6);
        foreach (var (centre, r) in rock.Craters)
        {
            var cc = Place(new[] { centre }, at, rock.Spin)[0];
            context.DrawEllipse(GetBrush(Color.FromRgb(36, 30, 25), 1), GetPen(Color.FromRgb(110, 95, 78), 0.8, 1), cc, r, r * 0.85);
        }
        // Crystals still inside glint here and there.
        for (int i = 0; i < Math.Min(rock.Crystals, 6); i++)
        {
            double a = rock.Spin + i * 2.4, d = rock.Radius * (0.2 + 0.1 * (i % 4));
            var p = at + new Vector(Math.Cos(a), Math.Sin(a)) * d;
            double glint = 0.4 + 0.6 * Math.Abs(Math.Sin(Clock * 3 + i));
            context.FillRectangle(GetBrush(GameEngine.CrystalColor, glint), new Rect(p.X - 1.2, p.Y - 1.2, 2.4, 2.4));
        }
    }

    private void DrawCrystal(DrawingContext context, Point at, double age)
    {
        if (!OnScreen(at, 20)) return;
        double s = 6, glint = 0.6 + 0.4 * Math.Sin(age * 10);
        var diamond = new[] { at + new Vector(0, -s), at + new Vector(s * 0.7, 0), at + new Vector(0, s), at + new Vector(-s * 0.7, 0) };
        DrawShape(context, diamond, GameEngine.CrystalColor, 0.9, GameEngine.CrystalColor, 1, 1.2);
        DrawGlowLine(context, at + new Vector(-12 * glint, 0), at + new Vector(12 * glint, 0), GameEngine.CrystalColor, 0.6, 1);
        DrawGlowLine(context, at + new Vector(0, -12 * glint), at + new Vector(0, 12 * glint), GameEngine.CrystalColor, 0.6, 1);
    }

    private static readonly Point[] WorkerBody = { new(10, 0), new(4, 7), new(-8, 6), new(-10, 0), new(-8, -6), new(4, -7) };
    private static readonly Point[] WorkerClawL = { new(4, -7), new(14, -10), new(16, -4), new(9, -4) };
    private static readonly Point[] WorkerClawR = { new(4, 7), new(14, 10), new(16, 4), new(9, 4) };

    private void DrawWorker(DrawingContext context, Worker w)
    {
        var at = ToScreen(w.Position);
        if (!OnScreen(at, 30)) return;
        var dark = Color.FromRgb(110, 20, 20);
        DrawShape(context, Place(WorkerClawL, at, w.Rotation), dark, 1, GameEngine.WorkerColor, 1, 1.3);
        DrawShape(context, Place(WorkerClawR, at, w.Rotation), dark, 1, GameEngine.WorkerColor, 1, 1.3);
        DrawShape(context, Place(WorkerBody, at, w.Rotation), Color.FromRgb(170, 35, 30), 1, GameEngine.WorkerColor, 1, 1.4);
        if (w.HasCrystal)
            DrawCrystal(context, Place(new[] { new Point(17, 0) }, at, w.Rotation)[0], Clock);
    }

    private static readonly Point[] WarriorShape = { new(16, 0), new(-6, 11), new(-12, 15), new(-8, 0), new(-12, -15), new(-6, -11) };

    private void DrawWarrior(DrawingContext context, Warrior w)
    {
        var at = ToScreen(w.Position);
        if (!OnScreen(at, 30)) return;
        DrawShape(context, Place(WarriorShape, at, w.Rotation), Color.FromRgb(20, 70, 110), 1, GameEngine.WarriorColor, 1, 1.5);
        context.DrawEllipse(GetBrush(Color.FromRgb(255, 230, 80), 0.9), null, Place(new[] { new Point(2, 0) }, at, w.Rotation)[0], 3.5, 3.5);
    }

    private void DrawShot(DrawingContext context, Shot shot)
    {
        var at = ToScreen(shot.Position);
        if (!OnScreen(at, 20)) return;
        var dir = shot.Velocity / Math.Max(shot.Velocity.Length, 1);
        var color = shot.FromPlayer ? Color.FromRgb(255, 250, 170) : Color.FromRgb(255, 90, 50);
        DrawGlowLine(context, at - dir * (shot.FromPlayer ? 12 : 7), at, color, 1, shot.FromPlayer ? 2.2 : 3);
    }

    private void DrawBomb(DrawingContext context, Sinibomb bomb)
    {
        for (int i = 1; i < bomb.Trail.Count; i++)
            DrawGlowLine(context, ToScreen(bomb.Trail[i - 1]), ToScreen(bomb.Trail[i]), GameEngine.BombColor, 0.7 * (1 - i / 10.0), 2.5);
        var at = ToScreen(bomb.Position);
        double pulse = 7 + 2 * Math.Sin(Clock * 25);
        context.DrawEllipse(GetBrush(GameEngine.BombColor, 0.35), null, at, pulse * 1.8, pulse * 1.8);
        context.DrawEllipse(GetBrush(Colors.White, 0.95), null, at, pulse * 0.6, pulse * 0.6);
    }

    private static readonly Point[] ShipShape = { new(18, 0), new(4, 5), new(-6, 14), new(-10, 12), new(-6, 4), new(-10, 0), new(-6, -4), new(-10, -12), new(-6, -14), new(4, -5) };

    private void DrawShip(DrawingContext context)
    {
        var ship = _engine.Ship;
        if (ship.Invulnerable > 0 && Blink(10)) return;
        var at = ToScreen(ship.Position);
        if (ship.IsThrusting && Blink(20))
        {
            var flame = Place(new[] { new Point(-10, -4), new Point(-22 - _random.NextDouble() * 6, 0), new Point(-10, 4) }, at, ship.Rotation);
            DrawShape(context, flame, Color.FromRgb(255, 150, 60), 0.8, Color.FromRgb(255, 210, 120), 0.9, 1.2);
        }
        DrawShape(context, Place(ShipShape, at, ship.Rotation), Color.FromRgb(120, 125, 140), 1, GameEngine.ShipColor, 1, 1.6);
        context.DrawEllipse(GetBrush(Color.FromRgb(255, 70, 60), 1), null, Place(new[] { new Point(4, 0) }, at, ship.Rotation)[0], 3, 3);
    }

    // ----- The Sinistar -----

    private void DrawSinistar(DrawingContext context)
    {
        var s = _engine.Sinistar;
        var at = ToScreen(s.Position);
        if (!OnScreen(at, 140)) return;

        bool any = s.PiecesBuilt > 0;
        if (!any && _engine.ZoneTimer <= 0)
        {
            // An empty building site: a faint ring where it will be.
            if (Blink(1)) DrawGlowPolygon(context, Ring(at, 108, 32), GameEngine.SinistarColor, 0.15, 1);
            return;
        }

        bool flash = s.HitFlash > 0;
        double jaw = s.IsAwake ? s.Mouth * 16 : 0;

        // The mouth's dark opening behind the jaw.
        if (s.Built[Sinistar.JawPiece] && jaw > 1)
            DrawShape(context, Place(new[] { new Point(-40, 50), new Point(40, 50), new Point(40, 56 + jaw), new Point(-40, 56 + jaw) }, at, 0),
                Color.FromRgb(20, 0, 0), 1, Color.FromRgb(120, 0, 0), 0.8, 1);

        for (int i = 0; i < Sinistar.Pieces; i++)
        {
            var shape = Sinistar.PieceShapes[i];
            var offset = i == Sinistar.JawPiece ? new Vector(0, jaw) : default;
            var placed = shape.Select(p => at + new Vector(p.X, p.Y) + offset).ToArray();
            if (s.Built[i])
            {
                var fill = flash ? Colors.White : i < 12 ? Color.FromRgb(96, 96, 104) : Color.FromRgb(120, 120, 128);
                DrawShape(context, placed, fill, 1, GameEngine.SinistarColor, 1, 1.4);
            }
            else
            {
                DrawGlowPolygon(context, placed, GameEngine.SinistarColor, 0.12, 1);
            }
        }

        // Teeth on the upper jaw.
        if (s.Built[18])
            for (int t = -36; t <= 36; t += 12)
                DrawGlowLine(context, at + new Vector(t, 38), at + new Vector(t, 50), Color.FromRgb(40, 40, 44), 1, 1.5);

        // The eyes burn red once their sockets are in place, brighter when it lives.
        for (int e = 0; e < 2; e++)
        {
            if (!s.Built[14 + e]) continue;
            var eye = at + new Vector(Sinistar.Eyes[e].X, Sinistar.Eyes[e].Y);
            double glow = s.IsAwake ? 0.75 + 0.25 * Math.Sin(Clock * 6) : 0.45;
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 30, 20), 0.3 * glow), null, eye, 18, 15);
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 60, 40), glow), null, eye, 9, 7);
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 220, 180), glow), null, eye, 3, 2.5);
        }
    }

    private static Point[] Ring(Point centre, double r, int n)
    {
        var points = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double a = i * Math.PI * 2 / n;
            points[i] = new Point(centre.X + Math.Cos(a) * r, centre.Y + Math.Sin(a) * r * 1.1);
        }
        return points;
    }
}
