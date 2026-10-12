using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace AVAMissile.Models;

/// <summary>The playfield: a fixed 1024×768 screen, with the ground along the bottom.</summary>
public static class Field
{
    public const double Width = 1024;
    public const double Height = 768;
    public const double Ground = 708;

    /// <summary>The offset from one point to another. (In Avalonia 12, Point minus Point is a Point, not a Vector.)</summary>
    public static Vector Sub(Point to, Point from) => new(to.X - from.X, to.Y - from.Y);

    public static double Distance(Point a, Point b) => Sub(a, b).Length;
}

/// <summary>A missile base: ten anti-ballistic missiles a wave, fired at the crosshair.</summary>
public class MissileBase
{
    public const int Capacity = 10;

    public string Name { get; }
    public Point Position { get; }
    public double Speed { get; }
    public int Missiles { get; set; }
    public bool Destroyed { get; set; }
    public bool CanFire => !Destroyed && Missiles > 0;

    public MissileBase(string name, Point position, double speed)
    {
        Name = name;
        Position = position;
        Speed = speed;
    }

    /// <summary>Where its missiles leave from: the top of its mound.</summary>
    public Point Muzzle => new(Position.X, Position.Y - 26);
}

public class City
{
    public Point Position { get; }
    public bool Alive { get; set; } = true;

    public City(Point position) => Position = position;
}

/// <summary>An anti-ballistic missile, climbing from a base to the point it was aimed at.</summary>
public class Abm
{
    public Point Start { get; }
    public Point Target { get; }
    public Point Position { get; set; }
    public double Speed { get; }
    public bool IsAlive { get; set; } = true;

    public Abm(Point start, Point target, double speed)
    {
        Start = start;
        Position = start;
        Target = target;
        Speed = speed;
    }
}

public enum IncomingKind
{
    Icbm,
    SmartBomb
}

/// <summary>An incoming warhead, drawing its trail down the sky toward a city or a base.</summary>
public class Incoming
{
    public IncomingKind Kind { get; }
    public Point TrailStart { get; set; }
    public Point Position { get; set; }
    public Point Target { get; }
    public double Speed { get; }
    public bool IsAlive { get; set; } = true;

    /// <summary>The height at which it splits into several, or NaN if it won't.</summary>
    public double SplitAt { get; set; } = double.NaN;

    public Incoming(IncomingKind kind, Point start, Point target, double speed)
    {
        Kind = kind;
        TrailStart = start;
        Position = start;
        Target = target;
        Speed = speed;
    }

    public int Points => Kind == IncomingKind.SmartBomb ? 125 : 25;
}

public enum FlyerKind
{
    Bomber,
    Satellite
}

/// <summary>A bomber or a killer satellite crossing the sky, dropping warheads as it goes.</summary>
public class Flyer
{
    public const int Points = 100;

    public FlyerKind Kind { get; }
    public Point Position { get; set; }
    public double Velocity { get; }
    public double DropTimer { get; set; }
    public bool IsAlive { get; set; } = true;

    public Flyer(FlyerKind kind, Point position, double velocity)
    {
        Kind = kind;
        Position = position;
        Velocity = velocity;
    }

    public double Radius => Kind == FlyerKind.Bomber ? 18 : 14;
}

/// <summary>
/// A fireball: it swells to full size, holds, and shrinks away. Anything that flies into it while it
/// burns is destroyed, and makes a fireball of its own.
/// </summary>
public class Blast
{
    public const double GrowTime = 0.55, HoldTime = 0.45, ShrinkTime = 0.75;
    public const double MaxRadius = 40;

    public Point Centre { get; }
    public double Age { get; set; }
    public double Size { get; }
    public bool FromGround { get; }

    public Blast(Point centre, double size = 1, bool fromGround = false)
    {
        Centre = centre;
        Size = size;
        FromGround = fromGround;
    }

    public bool IsFinished => Age >= GrowTime + HoldTime + ShrinkTime;

    public double Radius
    {
        get
        {
            double r = MaxRadius * Size;
            if (Age < GrowTime) return r * Age / GrowTime;
            if (Age < GrowTime + HoldTime) return r;
            return r * Math.Max(0, 1 - (Age - GrowTime - HoldTime) / ShrinkTime);
        }
    }
}

/// <summary>The colours of a pair of waves: sky, ground, the player's trails and the enemy's.</summary>
public record Palette(Color Sky, Color Ground, Color Friendly, Color Enemy, Color City)
{
    public static readonly Palette[] All =
    {
        new(Color.FromRgb(0, 0, 0), Color.FromRgb(230, 200, 30), Color.FromRgb(70, 120, 255), Color.FromRgb(255, 60, 50), Color.FromRgb(60, 200, 255)),
        new(Color.FromRgb(0, 0, 0), Color.FromRgb(40, 110, 255), Color.FromRgb(255, 230, 60), Color.FromRgb(255, 60, 50), Color.FromRgb(80, 255, 120)),
        new(Color.FromRgb(0, 0, 0), Color.FromRgb(230, 60, 60), Color.FromRgb(255, 255, 255), Color.FromRgb(60, 255, 120), Color.FromRgb(255, 220, 60)),
        new(Color.FromRgb(10, 20, 80), Color.FromRgb(240, 140, 30), Color.FromRgb(255, 255, 255), Color.FromRgb(255, 90, 200), Color.FromRgb(90, 255, 200)),
        new(Color.FromRgb(40, 0, 60), Color.FromRgb(60, 230, 90), Color.FromRgb(255, 230, 60), Color.FromRgb(90, 200, 255), Color.FromRgb(255, 120, 60)),
        new(Color.FromRgb(70, 20, 0), Color.FromRgb(200, 200, 220), Color.FromRgb(90, 255, 120), Color.FromRgb(255, 240, 80), Color.FromRgb(120, 160, 255))
    };

    /// <summary>The cabinet changed its colours every second wave.</summary>
    public static Palette ForWave(int wave) => All[((wave - 1) / 2) % All.Length];
}
