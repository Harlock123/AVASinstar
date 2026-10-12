using System;
using Avalonia;

namespace AVASinstar.Models;

public abstract class GameObject
{
    public Point Position { get; set; }
    public Vector Velocity { get; set; }
    public double Rotation { get; set; }
    public double Radius { get; protected set; }
    public bool IsAlive { get; set; } = true;

    protected GameObject(Point position, double radius)
    {
        Position = position;
        Radius = radius;
    }

    public Vector Heading => new(Math.Cos(Rotation), Math.Sin(Rotation));

    public virtual void Update(double deltaTime)
    {
        Position = World.Wrap(Position + Velocity * deltaTime);
    }

    public bool CollidesWith(GameObject other) =>
        World.Distance(Position, other.Position) < Radius + other.Radius;
}
