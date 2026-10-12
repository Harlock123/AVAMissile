using System;
using System.Linq;
using Avalonia;
using AVAMissile.Models;

namespace AVAMissile.Engine;

/// <summary>
/// Plays in attract mode. It picks the warhead that will land soonest, works out where to meet it (allowing
/// for the time its own missile takes to get there), slides the crosshair over and fires from the base
/// that will get there first. It's quick, but not so quick that nothing gets through.
/// </summary>
public class AttractPilot
{
    private static readonly Random Random = new();
    private double _cooldown;
    private Point? _aim;
    private int _base = GameEngine.NoFire;

    public void Reset()
    {
        _cooldown = 0;
        _aim = null;
    }

    public Orders Fly(GameEngine engine, double deltaTime)
    {
        _cooldown -= deltaTime;
        if (engine.Phase != WavePhase.Attack)
            return new Orders(null, GameEngine.NoFire);

        // Already lined up on a point: fire once the crosshair arrives.
        if (_aim is { } aim)
        {
            if (Field.Distance(engine.Crosshair, aim) < 6)
            {
                _aim = null;
                _cooldown = 0.35 + Random.NextDouble() * 0.4;
                return new Orders(aim, _base);
            }
            return new Orders(aim, GameEngine.NoFire);
        }
        if (_cooldown > 0)
            return new Orders(null, GameEngine.NoFire);

        // The threat that will land soonest and isn't already covered by a shot on its way.
        var threats = engine.Incoming
            .Select(m => (Missile: m, Eta: Field.Distance(m.Position, m.Target) / m.Speed))
            .Where(t => t.Missile.Position.Y > 60)
            .OrderBy(t => t.Eta)
            .ToList();
        foreach (var (m, _) in threats)
        {
            var heading = Field.Sub(m.Target, m.Position);
            var dir = heading / Math.Max(heading.Length, 1);
            var meet = m.Position;
            MissileBase? b = null;
            for (int i = 0; i < 4; i++)
            {
                b = engine.BestBaseFor(meet);
                if (b == null) break;
                double t = Field.Distance(b.Muzzle, meet) / b.Speed + 0.35;
                meet = m.Position + dir * m.Speed * t;
            }
            if (b == null) break;
            if (meet.Y > Field.Ground - 60) continue;

            bool covered = engine.Targets.Any(t => Field.Distance(t, meet) < 45) ||
                           engine.Blasts.Any(bl => !bl.FromGround && Field.Distance(bl.Centre, meet) < bl.Radius + 10);
            if (covered) continue;

            // A slightly shaky hand.
            _aim = meet + new Vector((Random.NextDouble() - 0.5) * 18, (Random.NextDouble() - 0.5) * 18);
            _base = Array.IndexOf(engine.Bases, b);
            return new Orders(_aim, GameEngine.NoFire);
        }

        // Nothing urgent: shoot at a bomber or satellite if one's about.
        var flyer = engine.Flyers.FirstOrDefault();
        if (flyer != null && engine.BestBaseFor(flyer.Position) is { } fb)
        {
            double t = Field.Distance(fb.Muzzle, flyer.Position) / fb.Speed + 0.3;
            _aim = flyer.Position + new Vector(flyer.Velocity * t, 0);
            _base = Array.IndexOf(engine.Bases, fb);
            return new Orders(_aim, GameEngine.NoFire);
        }
        return new Orders(null, GameEngine.NoFire);
    }
}
