using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVAMissile.Models;

namespace AVAMissile.Engine;

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

/// <summary>Where the crosshair should go, and which base (if any) to fire from this frame.</summary>
public readonly record struct Orders(Point? Crosshair, int FireBase);

/// <summary>
/// The game: three bases, six cities, the crosshair and the counter-missiles, the waves of attackers (in
/// GameEngine.Wave), scoring, and the title, attract and high-score cycle around it.
/// </summary>
public partial class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    public const int NoFire = -1, AutoFire = 3;
    private const double CrosshairSpeed = 620;
    private const double CeilingY = 40;

    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 75.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns
    private const double GameOverBeforeEntry = 2.5; // "THE END" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

    public GameState State { get; private set; }
    public int Score { get; private set; }

    public MissileBase[] Bases { get; } =
    {
        new("ALPHA", new Point(80, Field.Ground), 380),
        new("DELTA", new Point(512, Field.Ground), 760),
        new("OMEGA", new Point(944, Field.Ground), 380)
    };

    public City[] Cities { get; } = new[] { 190.0, 272, 354, 670, 752, 834 }.Select(x => new City(new Point(x, Field.Ground))).ToArray();

    public List<Abm> Abms { get; } = new();
    public List<Incoming> Incoming { get; } = new();
    public List<Flyer> Flyers { get; } = new();
    public List<Blast> Blasts { get; } = new();

    /// <summary>The points the counter-missiles in flight are aimed at, each marked with an X.</summary>
    public IEnumerable<Point> Targets => Abms.Select(a => a.Target);

    public Point Crosshair { get; private set; } = new(Field.Width / 2, Field.Height / 2);
    public double Clock { get; private set; }
    public double Shake { get; private set; }
    public bool IsInPlay => State is GameState.Playing or GameState.Attract;

    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;
    public InitialsEntry? Entry { get; private set; }
    public int HighlightIndex { get; private set; } = -1;
    public bool IsEntryPending => _entryPending;

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private readonly bool[] _wasFiring = new bool[4];
    private int _orderedFire = NoFire;

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
        Wave = 1;
        ResetField();
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

    public void StartNewGame() => BeginRun(GameState.Playing);

    /// <summary>The game plays itself, silently, until the cities are gone or time runs out.</summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopAllLoops();
        Sound.IsSilenced = false;
        Wave = 1;
        EndClock = -1;
        ResetField();
        _idleTimer = 0;
        HighlightIndex = -1;
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.StopAllLoops();
        Sound.IsSilenced = state == GameState.Attract;
        Score = 0;
        EndClock = -1;
        _nextBonusCity = BonusCityEvery;
        BonusCities = 0;
        Wave = 0;
        foreach (var city in Cities) city.Alive = true;
        _pilot.Reset();
        for (int i = 0; i < _wasFiring.Length; i++) _wasFiring[i] = true;
        StartWave();
    }

    private void ResetField()
    {
        Abms.Clear();
        Incoming.Clear();
        Flyers.Clear();
        Blasts.Clear();
        foreach (var city in Cities) city.Alive = true;
        foreach (var b in Bases)
        {
            b.Destroyed = false;
            b.Missiles = MissileBase.Capacity;
        }
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
        HighlightIndex = Table.Insert(initials, Score, Wave);
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
        bool start = Input.IsFiringAny;
        bool startPressed = start && !_wasStartPressed;
        _wasStartPressed = start;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                Input.TakePointerMove();
                return;

            case GameState.StartScreen:
            case GameState.HighScores:
                UpdateBlasts(deltaTime);
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
                UpdateBlasts(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                EndClock += deltaTime;
                UpdateBlasts(deltaTime);
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
                var orders = _pilot.Fly(this, deltaTime);
                if (orders.Crosshair is { } aim)
                    MoveCrosshairToward(aim, CrosshairSpeed * 1.4 * deltaTime);
                _orderedFire = orders.FireBase;
                break;

            default:
                SteerCrosshair(deltaTime);
                _orderedFire = PlayerFire();
                break;
        }

        Shake = Math.Max(0, Shake - deltaTime);
        if (_orderedFire != NoFire && Phase == WavePhase.Attack)
            Fire(_orderedFire);
        UpdateWave(deltaTime);
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
            for (int i = 0; i < _wasFiring.Length; i++) _wasFiring[i] = true;
        }
    }

    // ----- The crosshair and the bases -----

    private void SteerCrosshair(double deltaTime)
    {
        if (Input.TakePointerMove())
            Crosshair = Input.Pointer;
        Crosshair += Input.Stick * CrosshairSpeed * deltaTime;
        Crosshair = ClampCrosshair(Crosshair);
    }

    private void MoveCrosshairToward(Point target, double maxStep)
    {
        var d = Field.Sub(target, Crosshair);
        Crosshair = ClampCrosshair(d.Length <= maxStep ? target : Crosshair + d / d.Length * maxStep);
    }

    private static Point ClampCrosshair(Point p) =>
        new(Math.Clamp(p.X, 10, Field.Width - 10), Math.Clamp(p.Y, CeilingY, Field.Ground - 50));

    // Each of the three buttons fires from its own base; Space fires from whichever is best placed.
    private int PlayerFire()
    {
        var pressed = new[] { Input.IsFiringLeft, Input.IsFiringCentre, Input.IsFiringRight, Input.IsFiringAuto };
        int fire = NoFire;
        for (int i = 0; i < pressed.Length; i++)
        {
            if (pressed[i] && !_wasFiring[i] && fire == NoFire)
                fire = i;
            _wasFiring[i] = pressed[i];
        }
        return fire;
    }

    /// <summary>The base that would get a missile to a point soonest, of those that can fire.</summary>
    public MissileBase? BestBaseFor(Point target) =>
        Bases.Where(b => b.CanFire).OrderBy(b => Field.Distance(b.Muzzle, target) / b.Speed).FirstOrDefault();

    private void Fire(int which)
    {
        var target = Crosshair;
        var b = which == AutoFire ? BestBaseFor(target) : Bases[which];
        if (b == null || !b.CanFire)
        {
            Sound.PlayEmpty();
            return;
        }
        b.Missiles--;
        Abms.Add(new Abm(b.Muzzle, target, b.Speed));
        Sound.PlayLaunch();
        if (b.Missiles == 3)
            Sound.PlayLow();
    }
}
