using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVAMissile.Models;

namespace AVAMissile.Engine;

public enum WavePhase
{
    Intro,
    Attack,
    Tally
}

// The waves: what comes down, the fireballs, the counting up afterwards, and the end of everything.
public partial class GameEngine
{
    public const int BonusCityEvery = 10000;
    public const int MissileBonus = 5;
    public const int CityBonus = 100;
    private const double IntroTime = 2.6;

    public int Wave { get; private set; }
    public WavePhase Phase { get; private set; }
    public int Multiplier => Math.Min(6, (Wave + 1) / 2);
    public Palette Palette => Palette.ForWave(Math.Max(1, Wave));

    /// <summary>Cities in reserve, earned by score, to rebuild destroyed ones between waves.</summary>
    public int BonusCities { get; private set; }

    // The tally after a wave: how much has been counted so far.
    public int TalliedMissiles { get; private set; }
    public int TalliedCities { get; private set; }
    public int TallyPoints { get; private set; }
    public string? TallyNote { get; private set; }

    /// <summary>Seconds since THE END, during the last explosion.</summary>
    public double EndClock { get; private set; } = -1;

    private int _nextBonusCity;
    private double _phaseTimer;
    private int _toLaunch;
    private int _smartToLaunch;
    private int _flyersToLaunch;
    private double _salvoTimer, _flyerTimer;
    private List<Point> _waveTargets = new();
    private readonly List<int> _tallyMissiles = new();
    private int _tallyCityIndex;

    private double IncomingSpeed => Math.Min(150, 34 + 9 * (Wave - 1));

    private void StartWave()
    {
        Wave++;
        Phase = WavePhase.Intro;
        _phaseTimer = IntroTime;
        Abms.Clear();
        Incoming.Clear();
        Flyers.Clear();
        Blasts.Clear();
        foreach (var b in Bases)
        {
            b.Destroyed = false;
            b.Missiles = MissileBase.Capacity;
        }

        _toLaunch = Math.Min(30, 10 + 2 * Wave);
        _smartToLaunch = Wave >= 6 ? Math.Min(5, 1 + (Wave - 6) / 2) : 0;
        _flyersToLaunch = Wave >= 2 ? Math.Min(4, 1 + Wave / 3) : 0;
        _salvoTimer = 0.5;
        _flyerTimer = 6 + Random.NextDouble() * 4;

        // As on the cabinet, each wave goes for at most three of the cities, plus the bases.
        var cities = Cities.Where(c => c.Alive).OrderBy(_ => Random.Next()).Take(3).Select(c => c.Position).ToList();
        if (cities.Count < 3)
            cities.AddRange(Cities.Where(c => !c.Alive).OrderBy(_ => Random.Next()).Take(3 - cities.Count).Select(c => c.Position));
        _waveTargets = cities.Concat(Bases.Select(b => b.Position)).ToList();

        Sound.PlayAlert();
    }

    private void UpdateWave(double deltaTime)
    {
        switch (Phase)
        {
            case WavePhase.Intro:
                _phaseTimer -= deltaTime;
                if (_phaseTimer <= 0)
                    Phase = WavePhase.Attack;
                break;

            case WavePhase.Attack:
                Launch(deltaTime);
                UpdateAbms(deltaTime);
                UpdateIncoming(deltaTime);
                UpdateFlyers(deltaTime);
                UpdateBlasts(deltaTime);
                CheckHits();
                if (_toLaunch == 0 && _smartToLaunch == 0 && _flyersToLaunch == 0 &&
                    Incoming.Count == 0 && Flyers.Count == 0 && Abms.Count == 0 && Blasts.Count == 0)
                    BeginTally();
                break;

            case WavePhase.Tally:
                UpdateTally(deltaTime);
                break;
        }
    }

    // ----- What comes down -----

    private Point RandomTarget() => _waveTargets[Random.Next(_waveTargets.Count)] + new Vector((Random.NextDouble() - 0.5) * 16, 0);

    private void Launch(double deltaTime)
    {
        // Warheads come in salvos of a few at a time, from along the top of the screen.
        _salvoTimer -= deltaTime;
        if (_salvoTimer <= 0 && (_toLaunch > 0 || _smartToLaunch > 0) && Incoming.Count < 8 + Wave / 3)
        {
            int salvo = Math.Min(_toLaunch, 2 + Random.Next(3));
            for (int i = 0; i < salvo; i++)
            {
                var start = new Point(20 + Random.NextDouble() * (Field.Width - 40), 0);
                var missile = new Incoming(IncomingKind.Icbm, start, RandomTarget(), IncomingSpeed * (0.85 + Random.NextDouble() * 0.3));
                // From the second wave, some split partway down, like MIRVs.
                if (Wave >= 2 && Random.NextDouble() < 0.25)
                    missile.SplitAt = 240 + Random.NextDouble() * 200;
                Incoming.Add(missile);
            }
            _toLaunch -= salvo;

            if (_smartToLaunch > 0 && Random.NextDouble() < 0.4)
            {
                var start = new Point(100 + Random.NextDouble() * (Field.Width - 200), 0);
                Incoming.Add(new Incoming(IncomingKind.SmartBomb, start, RandomTarget(), IncomingSpeed * 0.8));
                _smartToLaunch--;
                Sound.StartSmartBomb();
            }
            else if (_toLaunch == 0 && _smartToLaunch > 0)
            {
                var start = new Point(100 + Random.NextDouble() * (Field.Width - 200), 0);
                Incoming.Add(new Incoming(IncomingKind.SmartBomb, start, RandomTarget(), IncomingSpeed * 0.8));
                _smartToLaunch--;
                Sound.StartSmartBomb();
            }
            _salvoTimer = Math.Max(1.4, 4.2 - 0.18 * Wave) + Random.NextDouble() * 1.5;
        }

        // Now and then a bomber, or a killer satellite, crosses the sky.
        _flyerTimer -= deltaTime;
        if (_flyerTimer <= 0 && _flyersToLaunch > 0 && Flyers.Count == 0)
        {
            bool fromLeft = Random.Next(2) == 0;
            var kind = Wave % 2 == 0 ? FlyerKind.Bomber : FlyerKind.Satellite;
            Flyers.Add(new Flyer(kind, new Point(fromLeft ? -30 : Field.Width + 30, 170 + Random.NextDouble() * 170), (fromLeft ? 1 : -1) * (60 + 4 * Wave))
            {
                DropTimer = 1.5 + Random.NextDouble() * 2
            });
            _flyersToLaunch--;
            _flyerTimer = 8 + Random.NextDouble() * 6;
            Sound.StartFlyer();
        }
    }

    private void UpdateIncoming(double deltaTime)
    {
        var splits = new List<Incoming>();
        foreach (var m in Incoming)
        {
            var to = Field.Sub(m.Target, m.Position);
            double distance = to.Length;
            var dir = to / Math.Max(distance, 1e-6);

            if (m.Kind == IncomingKind.SmartBomb)
            {
                // A smart bomb sidesteps any fireball in its way.
                foreach (var blast in Blasts.Where(b => !b.FromGround))
                {
                    var off = Field.Sub(m.Position, blast.Centre);
                    double d = off.Length;
                    if (d < blast.Radius + 45 && d > 0.1)
                        dir += off / d * 1.6;
                }
                dir /= Math.Max(dir.Length, 1e-6);
            }

            double step = m.Speed * deltaTime;
            if (distance <= step)
            {
                m.Position = m.Target;
                m.IsAlive = false;
                Detonate(m.Target);
                continue;
            }
            m.Position += dir * step;

            if (!double.IsNaN(m.SplitAt) && m.Position.Y >= m.SplitAt)
            {
                // A MIRV splits: the original carries on, and two or three more peel off at other targets.
                m.SplitAt = double.NaN;
                int extra = 1 + Random.Next(2);
                for (int i = 0; i < extra; i++)
                {
                    var child = new Incoming(IncomingKind.Icbm, m.Position, RandomTarget(), m.Speed);
                    splits.Add(child);
                }
            }
        }
        Incoming.AddRange(splits);
        Incoming.RemoveAll(m => !m.IsAlive);
        if (!Incoming.Any(m => m.Kind == IncomingKind.SmartBomb))
            Sound.StopSmartBomb();
    }

    /// <summary>A warhead reaches the ground: whatever it was aimed at is gone.</summary>
    private void Detonate(Point at)
    {
        Blasts.Add(new Blast(at, 1.1, fromGround: true));
        Shake = Math.Max(Shake, 0.3);

        foreach (var city in Cities)
        {
            if (city.Alive && Math.Abs(city.Position.X - at.X) < 32)
            {
                city.Alive = false;
                Sound.PlayCityBoom();
                return;
            }
        }
        foreach (var b in Bases)
        {
            if (!b.Destroyed && Math.Abs(b.Position.X - at.X) < 36)
            {
                b.Destroyed = true;
                b.Missiles = 0;
                Sound.PlayBaseBoom();
                return;
            }
        }
        Sound.PlayBoom();
    }

    private void UpdateFlyers(double deltaTime)
    {
        foreach (var f in Flyers)
        {
            f.Position += new Vector(f.Velocity * deltaTime, Math.Sin(Clock * 2 + f.Position.X * 0.01) * 6 * deltaTime);
            f.DropTimer -= deltaTime;
            if (f.DropTimer <= 0 && f.Position.X > 60 && f.Position.X < Field.Width - 60)
            {
                f.DropTimer = 2.5 + Random.NextDouble() * 2.5;
                Incoming.Add(new Incoming(IncomingKind.Icbm, f.Position, RandomTarget(), IncomingSpeed));
            }
            if (f.Position.X < -60 || f.Position.X > Field.Width + 60)
                f.IsAlive = false;
        }
        Flyers.RemoveAll(f => !f.IsAlive);
        if (Flyers.Count == 0)
            Sound.StopFlyer();
    }

    // ----- Counter-missiles and fireballs -----

    private void UpdateAbms(double deltaTime)
    {
        foreach (var a in Abms)
        {
            var to = Field.Sub(a.Target, a.Position);
            double step = a.Speed * deltaTime;
            if (to.Length <= step)
            {
                a.IsAlive = false;
                Blasts.Add(new Blast(a.Target));
                Sound.PlayBoom();
                continue;
            }
            a.Position += to / to.Length * step;
        }
        Abms.RemoveAll(a => !a.IsAlive);
    }

    private void UpdateBlasts(double deltaTime)
    {
        foreach (var b in Blasts) b.Age += deltaTime;
        Blasts.RemoveAll(b => b.IsFinished);
    }

    /// <summary>
    /// Anything inside a fireball is destroyed, and goes up in a fireball of its own, so one well-placed
    /// shot can set off a chain.
    /// </summary>
    private void CheckHits()
    {
        var newBlasts = new List<Blast>();
        foreach (var blast in Blasts)
        {
            double r = blast.Radius;
            if (r <= 1) continue;
            foreach (var m in Incoming)
            {
                if (m.IsAlive && Field.Distance(m.Position, blast.Centre) < r)
                {
                    m.IsAlive = false;
                    AddScore(m.Points * Multiplier);
                    newBlasts.Add(new Blast(m.Position, 0.8));
                }
            }
            foreach (var f in Flyers)
            {
                if (f.IsAlive && Field.Distance(f.Position, blast.Centre) < r + f.Radius * 0.6)
                {
                    f.IsAlive = false;
                    AddScore(Flyer.Points * Multiplier);
                    newBlasts.Add(new Blast(f.Position, 1.1));
                    Sound.PlayFlyerBoom();
                }
            }
        }
        if (newBlasts.Count > 0)
            Sound.PlayBoom();
        Blasts.AddRange(newBlasts);
        Incoming.RemoveAll(m => !m.IsAlive);
        Flyers.RemoveAll(f => !f.IsAlive);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextBonusCity)
        {
            _nextBonusCity += BonusCityEvery;
            BonusCities++;
        }
    }

    // ----- The tally, and the end -----

    private void BeginTally()
    {
        Phase = WavePhase.Tally;
        Sound.StopAllLoops();
        TalliedMissiles = TalliedCities = TallyPoints = 0;
        TallyNote = null;
        _tallyMissiles.Clear();
        foreach (var b in Bases)
            for (int i = 0; i < b.Missiles; i++)
                _tallyMissiles.Add(Array.IndexOf(Bases, b));
        _tallyCityIndex = 0;
        _phaseTimer = 0.6;
    }

    // Unused missiles count up first, then the surviving cities, each with its click; then a bonus city
    // is put up if one is owed and needed, and on to the next wave, or to THE END.
    private void UpdateTally(double deltaTime)
    {
        _phaseTimer -= deltaTime;
        if (_phaseTimer > 0) return;

        if (_tallyMissiles.Count > 0)
        {
            var b = Bases[_tallyMissiles[0]];
            _tallyMissiles.RemoveAt(0);
            b.Missiles--;
            TalliedMissiles++;
            int points = MissileBonus * Multiplier;
            TallyPoints += points;
            AddScore(points);
            Sound.PlayTallyMissile();
            _phaseTimer = 0.06;
            return;
        }

        while (_tallyCityIndex < Cities.Length && !Cities[_tallyCityIndex].Alive)
            _tallyCityIndex++;
        if (_tallyCityIndex < Cities.Length)
        {
            _tallyCityIndex++;
            TalliedCities++;
            int points = CityBonus * Multiplier;
            TallyPoints += points;
            AddScore(points);
            Sound.PlayTallyCity();
            _phaseTimer = 0.28;
            return;
        }

        if (TallyNote == null)
        {
            // Rebuild a lost city from the reserve, if there's one to rebuild.
            var ruin = Cities.FirstOrDefault(c => !c.Alive);
            if (ruin != null && BonusCities > 0)
            {
                BonusCities--;
                ruin.Alive = true;
                TallyNote = "BONUS CITY";
                Sound.PlayBonusCity();
            }
            else
            {
                TallyNote = "";
            }
            _phaseTimer = 1.6;
            return;
        }

        if (Cities.All(c => !c.Alive))
            TheEnd();
        else
            StartWave();
    }

    /// <summary>Every city is gone. There is no winner: just a great white blast, and THE END.</summary>
    private void TheEnd()
    {
        EndClock = 0;
        Sound.StopAllLoops();
        Sound.PlayTheEnd();
        Shake = 1.2;

        if (State == GameState.Attract)
        {
            _attractTimer = Math.Min(_attractTimer, 4);
            State = GameState.GameOver;
            _gameOverTimer = 4;
            _entryPending = false;
            _idleTimer = GameOverIdleDelay - 4;
            return;
        }
        State = GameState.GameOver;
        _gameOverTimer = GameOverInputDelay;
        _idleTimer = 0;
        _entryPending = Table.Qualifies(Score);
    }
}
