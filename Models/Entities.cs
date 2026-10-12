using System;
using System.Collections.Generic;
using Avalonia;

namespace AVASinstar.Models;

/// <summary>The player's ship: it turns to point where the stick points and flies that way, coasting when let go.</summary>
public class PlayerShip : GameObject
{
    public const double MaxSpeed = 520;
    public const double Acceleration = 900;
    public const double Drag = 0.6;
    public const double FireInterval = 0.11;
    public const int MaxBombs = 20;

    public int Bombs { get; set; }
    public double FireTimer { get; set; }
    public double BombTimer { get; set; }
    public double Invulnerable { get; set; }
    public bool IsThrusting { get; private set; }

    public PlayerShip(Point position) : base(position, 13)
    {
        Rotation = -Math.PI / 2;
    }

    /// <summary>Steers by the stick: full deflection asks for full speed in that direction.</summary>
    public void Fly(Vector stick, double deltaTime)
    {
        IsThrusting = stick.Length > 0.05;
        if (IsThrusting)
        {
            double wanted = Math.Atan2(stick.Y, stick.X);
            Rotation = World.TurnToward(Rotation, wanted, 9 * deltaTime);
            var target = new Vector(Math.Cos(Rotation), Math.Sin(Rotation)) * MaxSpeed * Math.Min(1, stick.Length);
            var diff = target - Velocity;
            double step = Acceleration * deltaTime;
            Velocity = diff.Length <= step ? target : Velocity + diff / diff.Length * step;
        }
        else
        {
            Velocity *= Math.Max(0, 1 - Drag * deltaTime);
        }
        FireTimer = Math.Max(0, FireTimer - deltaTime);
        BombTimer = Math.Max(0, BombTimer - deltaTime);
        Invulnerable = Math.Max(0, Invulnerable - deltaTime);
        Update(deltaTime);
    }

    public Point Nose => World.Wrap(Position + Heading * 16);
}

/// <summary>A planetoid: a drifting, tumbling rock seeded with crystals. Shooting it knocks them loose.</summary>
public class Planetoid : GameObject
{
    public Point[] Outline { get; }
    public (Point Centre, double Radius)[] Craters { get; }
    public int Crystals { get; set; }
    public int MaxCrystals { get; }
    public double Spin { get; set; }
    public double SpinRate { get; }
    public double HitFlash { get; set; }

    public Planetoid(Point position, double radius, int crystals, Random random) : base(position, radius)
    {
        Crystals = crystals;
        MaxCrystals = crystals;
        SpinRate = (random.NextDouble() - 0.5) * 0.4;
        Velocity = new Vector(random.NextDouble() - 0.5, random.NextDouble() - 0.5) * 30;

        int points = 11 + random.Next(5);
        Outline = new Point[points];
        for (int i = 0; i < points; i++)
        {
            double a = i * Math.PI * 2 / points;
            double r = radius * (0.78 + random.NextDouble() * 0.3);
            Outline[i] = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
        }
        Craters = new (Point, double)[2 + random.Next(3)];
        for (int i = 0; i < Craters.Length; i++)
        {
            double a = random.NextDouble() * Math.PI * 2, d = random.NextDouble() * radius * 0.5;
            Craters[i] = (new Point(Math.Cos(a) * d, Math.Sin(a) * d), radius * (0.1 + random.NextDouble() * 0.14));
        }
    }

    public override void Update(double deltaTime)
    {
        Spin += SpinRate * deltaTime;
        HitFlash = Math.Max(0, HitFlash - deltaTime);
        base.Update(deltaTime);
    }
}

/// <summary>A crystal knocked out of a planetoid. Picked up, it becomes a Sinibomb; carried off by a worker, a piece of the Sinistar.</summary>
public class Crystal : GameObject
{
    public const double Life = 14;
    public double Age { get; private set; }
    public bool IsClaimed { get; set; }

    public Crystal(Point position, Vector velocity) : base(position, 6)
    {
        Velocity = velocity;
    }

    public override void Update(double deltaTime)
    {
        Age += deltaTime;
        if (Age >= Life) IsAlive = false;
        Velocity *= Math.Max(0, 1 - 0.8 * deltaTime);
        base.Update(deltaTime);
    }
}

public enum WorkerTask
{
    Seeking,
    Mining,
    Carrying
}

/// <summary>A worker: harmless itself, it gathers crystals and carries them off to build the Sinistar.</summary>
public class Worker : GameObject
{
    public const int Points = 150;

    public WorkerTask Task { get; set; }
    public Crystal? Target { get; set; }
    public Planetoid? Rock { get; set; }
    public double TaskTime { get; set; }
    public double Wobble { get; set; }
    public bool HasCrystal => Task == WorkerTask.Carrying;

    public Worker(Point position) : base(position, 11) { }
}

/// <summary>A warrior: it hunts the player, and shoots planetoids to free crystals for the workers.</summary>
public class Warrior : GameObject
{
    public const int Points = 500;

    public double FireTimer { get; set; }
    public double Strafe { get; set; } = 1;
    public Planetoid? Rock { get; set; }

    public Warrior(Point position) : base(position, 13) { }
}

/// <summary>A shot, from the player or a warrior.</summary>
public class Shot : GameObject
{
    public double Life { get; set; }
    public bool FromPlayer { get; }

    public Shot(Point position, Vector velocity, double life, bool fromPlayer) : base(position, 3)
    {
        Velocity = velocity;
        Life = life;
        FromPlayer = fromPlayer;
    }

    public override void Update(double deltaTime)
    {
        Life -= deltaTime;
        if (Life <= 0) IsAlive = false;
        base.Update(deltaTime);
    }
}

/// <summary>A Sinibomb: it hunts down the Sinistar, wherever it is, and blasts pieces off it.</summary>
public class Sinibomb : GameObject
{
    public const double Speed = 620;
    public double Age { get; set; }
    public List<Point> Trail { get; } = new();

    public Sinibomb(Point position, double heading, Vector shipVelocity) : base(position, 8)
    {
        Rotation = heading;
        Velocity = Heading * Speed * 0.6 + shipVelocity * 0.5;
    }
}

/// <summary>
/// The Sinistar: a great skull face, built a piece at a time by the workers. With all twenty pieces in
/// place it comes alive and hunts the player; thirteen Sinibombs destroy it.
/// </summary>
public class Sinistar : GameObject
{
    public const int Pieces = 20;
    public const int BombsToKill = 13;
    public const int Points = 15000;
    public const int PiecePoints = 500;

    /// <summary>Which of the face's pieces are in place.</summary>
    public bool[] Built { get; } = new bool[Pieces];

    /// <summary>The order the workers add pieces in, shuffled for each new Sinistar.</summary>
    public int[] BuildOrder { get; }

    public int PiecesBuilt
    {
        get
        {
            int n = 0;
            foreach (bool b in Built) if (b) n++;
            return n;
        }
    }

    /// <summary>Complete and awake: it hunts the player.</summary>
    public bool IsAwake { get; set; }
    public bool IsComplete => PiecesBuilt == Pieces;
    public int BombHits { get; set; }
    public double Mouth { get; set; }
    public double HitFlash { get; set; }
    public double TauntTimer { get; set; }

    public Sinistar(Point position, Random random) : base(position, 105)
    {
        BuildOrder = new int[Pieces];
        for (int i = 0; i < Pieces; i++) BuildOrder[i] = i;
        for (int i = Pieces - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (BuildOrder[i], BuildOrder[j]) = (BuildOrder[j], BuildOrder[i]);
        }
    }

    /// <summary>Adds the next piece. Returns false if it was already whole.</summary>
    public bool AddPiece()
    {
        foreach (int i in BuildOrder)
        {
            if (!Built[i])
            {
                Built[i] = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>Knocks off pieces, newest first. Returns how many came off.</summary>
    public int RemovePieces(int count)
    {
        int removed = 0;
        for (int k = Pieces - 1; k >= 0 && removed < count; k--)
        {
            int i = BuildOrder[k];
            if (Built[i])
            {
                Built[i] = false;
                removed++;
            }
        }
        return removed;
    }

    /// <summary>The eye centres, for the red glow. (Declared before the shapes, which are built round them.)</summary>
    public static readonly Point[] Eyes = { new(-36, -14), new(36, -14) };

    /// <summary>
    /// The face's twenty pieces, as polygons round the centre (about 110 across each way): twelve plates
    /// round the edge of the skull, then the brows, the eye sockets, the cheekbones, the nose, the upper
    /// teeth and the jaw.
    /// </summary>
    public static readonly Point[][] PieceShapes = BuildShapes();

    private static Point[][] BuildShapes()
    {
        var shapes = new List<Point[]>();
        const double rx = 100, ry = 112;

        // Twelve plates round the outside of the skull.
        for (int k = 0; k < 12; k++)
        {
            double a0 = -Math.PI / 2 + k * Math.PI * 2 / 12 + 0.02, a1 = a0 + Math.PI * 2 / 12 - 0.04;
            var plate = new List<Point>();
            for (int s = 0; s <= 3; s++)
            {
                double a = a0 + (a1 - a0) * s / 3;
                plate.Add(new Point(Math.Cos(a) * rx, Math.Sin(a) * ry));
            }
            for (int s = 3; s >= 0; s--)
            {
                double a = a0 + (a1 - a0) * s / 3;
                plate.Add(new Point(Math.Cos(a) * rx * 0.7, Math.Sin(a) * ry * 0.7));
            }
            shapes.Add(plate.ToArray());
        }

        Point P(double x, double y) => new(x, y);
        // Brows.
        shapes.Add(new[] { P(-66, -52), P(-6, -56), P(-8, -36), P(-62, -32) });
        shapes.Add(new[] { P(66, -52), P(6, -56), P(8, -36), P(62, -32) });
        // Eye sockets: hexagons round the eyes.
        foreach (var eye in Eyes)
        {
            var hex = new Point[6];
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                hex[i] = new Point(eye.X + Math.Cos(a) * 22, eye.Y + Math.Sin(a) * 18);
            }
            shapes.Add(hex);
        }
        // Cheekbones.
        shapes.Add(new[] { P(-66, 4), P(-30, 14), P(-24, 36), P(-62, 30) });
        shapes.Add(new[] { P(66, 4), P(30, 14), P(24, 36), P(62, 30) });
        // Nose.
        shapes.Add(new[] { P(0, -2), P(14, 30), P(-14, 30) });
        // Upper teeth.
        shapes.Add(new[] { P(-46, 38), P(46, 38), P(42, 50), P(-42, 50) });
        // Jaw.
        shapes.Add(new[] { P(-42, 56), P(42, 56), P(30, 72), P(-30, 72) });
        return shapes.ToArray();
    }

    public const int JawPiece = 19;
}
