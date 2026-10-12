using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVAMissile.Engine;
using AVAMissile.Models;

namespace AVAMissile.Views;

// The sky, the ground with its cities and bases, the trails, the fireballs, and THE END.
public partial class GameCanvas
{
    private void DrawBattle(DrawingContext context)
    {
        var pal = _engine.Palette;
        context.FillRectangle(GetBrush(pal.Sky, 1), new Rect(0, 0, ScreenWidth, ScreenHeight));
        DrawGround(context, pal);
        foreach (var city in _engine.Cities) DrawCity(context, city, pal);
        for (int i = 0; i < _engine.Bases.Length; i++) DrawBase(context, _engine.Bases[i], pal);

        foreach (var m in _engine.Incoming) DrawIncoming(context, m, pal);
        foreach (var f in _engine.Flyers) DrawFlyer(context, f, pal);
        foreach (var a in _engine.Abms)
        {
            DrawGlowLine(context, a.Start, a.Position, pal.Friendly, 0.9, 1.5);
            context.FillRectangle(GetBrush(Colors.White, 1), new Rect(a.Position.X - 1.5, a.Position.Y - 1.5, 3, 3));
        }
        foreach (var t in _engine.Targets) DrawTargetMark(context, t, pal);
        foreach (var b in _engine.Blasts) DrawBlast(context, b, pal);

        if (_engine.IsInPlay && _engine.Phase == WavePhase.Attack)
            DrawCrosshair(context, pal);
        if (_engine.IsInPlay && _engine.Phase == WavePhase.Intro)
            DrawIntro(context, pal);
        if (_engine.IsInPlay && _engine.Phase == WavePhase.Tally)
            DrawTally(context, pal);
    }

    // The ground: a strip along the bottom, rising into a mound under each base.
    private void DrawGround(DrawingContext context, Palette pal)
    {
        double g = Field.Ground;
        var points = new System.Collections.Generic.List<Point> { new(0, ScreenHeight), new(0, g + 6) };
        foreach (var b in _engine.Bases)
        {
            double x = b.Position.X;
            points.Add(new Point(x - 70, g + 6));
            points.Add(new Point(x - 40, g - 24));
            points.Add(new Point(x + 40, g - 24));
            points.Add(new Point(x + 70, g + 6));
        }
        points.Add(new Point(ScreenWidth, g + 6));
        points.Add(new Point(ScreenWidth, ScreenHeight));
        DrawShape(context, points, pal.Ground, 1, pal.Ground, 0.4, 1);
    }

    // A city: a little skyline of blocks, or a smouldering heap when it's gone.
    private void DrawCity(DrawingContext context, City city, Palette pal)
    {
        var c = city.Position;
        if (!city.Alive)
        {
            var rubble = new[] { c + new Vector(-24, 6), c + new Vector(-14, -2), c + new Vector(-4, 2), c + new Vector(6, -4), c + new Vector(18, 1), c + new Vector(24, 6) };
            DrawShape(context, rubble, Color.FromRgb(70, 60, 50), 1, Color.FromRgb(120, 100, 80), 0.6, 1);
            return;
        }
        double[][] blocks = { new[] { -24.0, 14 }, new[] { -18.0, 26 }, new[] { -10.0, 18 }, new[] { -3.0, 30 }, new[] { 5.0, 20 }, new[] { 12.0, 24 }, new[] { 19.0, 12 } };
        foreach (var block in blocks)
        {
            var r = new Rect(c.X + block[0], c.Y + 6 - block[1], 6.5, block[1]);
            context.FillRectangle(GetBrush(pal.City, 1), r);
        }
        // Lit windows.
        for (int i = 0; i < 8; i++)
            context.FillRectangle(GetBrush(pal.Sky, 0.8), new Rect(c.X - 20 + i * 5.5, c.Y - 4 - (i % 3) * 6, 1.6, 2));
    }

    // A base: its remaining missiles stacked on top of the mound, LOW or OUT underneath.
    private void DrawBase(DrawingContext context, MissileBase b, Palette pal)
    {
        var top = new Point(b.Position.X, Field.Ground - 26);
        if (b.Destroyed)
        {
            context.DrawEllipse(GetBrush(Color.FromRgb(40, 30, 20), 1), null, top + new Vector(0, 6), 26, 7);
            return;
        }

        // Ten missiles in a pyramid: 4, 3, 2, 1 from the bottom.
        int left = b.Missiles, drawn = 0;
        int[] rows = { 4, 3, 2, 1 };
        for (int row = 0; row < rows.Length; row++)
        for (int i = 0; i < rows[row]; i++)
        {
            if (drawn++ >= left) break;
            double x = top.X + (i - (rows[row] - 1) / 2.0) * 12, y = top.Y - 4 - row * 9;
            var shape = new[] { new Point(x - 3.5, y), new Point(x, y - 7), new Point(x + 3.5, y) };
            DrawShape(context, shape, pal.Friendly, 1, pal.Friendly, 0.6, 1);
        }

        string? label = b.Missiles == 0 ? "OUT" : b.Missiles <= 3 ? "LOW" : null;
        if (label != null && _engine.IsInPlay && Blink(3))
            DrawCentredText(context, label, 14, Field.Ground + 16, Colors.Black, b.Position.X);
    }

    private void DrawIncoming(DrawingContext context, Incoming m, Palette pal)
    {
        bool flash = (int)(Clock * 20) % 2 == 0;
        if (m.Kind == IncomingKind.SmartBomb)
        {
            // A smart bomb: a spinning diamond, no trail.
            double a = Clock * 8;
            var d = new[] { 0, 1, 2, 3 }.Select(i => m.Position + new Vector(Math.Cos(a + i * Math.PI / 2) * 8, Math.Sin(a + i * Math.PI / 2) * 8)).ToArray();
            DrawShape(context, d, flash ? Colors.White : pal.Enemy, 1, pal.Enemy, 1, 1.2);
            return;
        }
        DrawGlowLine(context, m.TrailStart, m.Position, pal.Enemy, 0.85, 1.4);
        context.FillRectangle(GetBrush(flash ? Colors.White : pal.Enemy, 1), new Rect(m.Position.X - 1.5, m.Position.Y - 1.5, 3, 3));
    }

    private void DrawFlyer(DrawingContext context, Flyer f, Palette pal)
    {
        var c = f.Position;
        double dir = Math.Sign(f.Velocity);
        if (f.Kind == FlyerKind.Bomber)
        {
            // A bomber, side on: fuselage, swept wing and tail.
            Point P(double x, double y) => new(c.X + x * dir, c.Y + y);
            var body = new[] { P(20, 0), P(14, -4), P(-16, -4), P(-22, -10), P(-24, -10), P(-22, 2), P(14, 3) };
            var wing = new[] { P(4, -1), P(-8, 8), P(-12, 8), P(-4, -1) };
            DrawShape(context, body, pal.Enemy, 1, Colors.White, 0.6, 1);
            DrawShape(context, wing, pal.Enemy, 1, Colors.White, 0.6, 1);
        }
        else
        {
            // A killer satellite: a box with two solar panels and an antenna.
            context.FillRectangle(GetBrush(pal.Enemy, 1), new Rect(c.X - 6, c.Y - 6, 12, 12));
            context.FillRectangle(GetBrush(pal.Friendly, 0.8), new Rect(c.X - 20, c.Y - 3, 12, 6));
            context.FillRectangle(GetBrush(pal.Friendly, 0.8), new Rect(c.X + 8, c.Y - 3, 12, 6));
            DrawGlowLine(context, c + new Vector(0, -6), c + new Vector(0, -14), Colors.White, 0.8, 1);
        }
    }

    private void DrawTargetMark(DrawingContext context, Point t, Palette pal)
    {
        DrawGlowLine(context, t + new Vector(-5, -5), t + new Vector(5, 5), pal.Friendly, 1, 1.5);
        DrawGlowLine(context, t + new Vector(-5, 5), t + new Vector(5, -5), pal.Friendly, 1, 1.5);
    }

    // A fireball flickers through colours as it burns, as on the cabinet.
    private void DrawBlast(DrawingContext context, Blast b, Palette pal)
    {
        double r = b.Radius;
        if (r < 0.5) return;
        var cycle = new[] { Colors.White, pal.Enemy, pal.Friendly, pal.City, Color.FromRgb(255, 230, 60) };
        var color = cycle[(int)(Clock * 22 + b.Centre.X) % cycle.Length];
        context.DrawEllipse(GetBrush(color, 0.95), null, b.Centre, r, r);
    }

    private void DrawCrosshair(DrawingContext context, Palette pal)
    {
        var c = _engine.Crosshair;
        DrawGlowLine(context, c + new Vector(-9, 0), c + new Vector(9, 0), pal.Friendly, 1, 2);
        DrawGlowLine(context, c + new Vector(0, -9), c + new Vector(0, 9), pal.Friendly, 1, 2);
    }

    // Before each wave: the multiplier, and arrows over the cities to defend.
    private void DrawIntro(DrawingContext context, Palette pal)
    {
        DrawCentredText(context, $"WAVE {_engine.Wave}", 34, 250, pal.Friendly);
        DrawCentredText(context, $"{_engine.Multiplier} X POINTS", 24, 300, Colors.White);
        if (Blink(3))
        {
            DrawCentredText(context, "DEFEND", 22, 400, pal.Enemy);
            DrawCentredText(context, "CITIES", 22, 428, pal.Enemy);
            foreach (var city in _engine.Cities.Where(c => c.Alive))
            {
                var p = city.Position + new Vector(0, -60);
                DrawGlowLine(context, p, p + new Vector(0, 26), pal.Enemy, 1, 2);
                DrawGlowLine(context, p + new Vector(-6, 18), p + new Vector(0, 26), pal.Enemy, 1, 2);
                DrawGlowLine(context, p + new Vector(6, 18), p + new Vector(0, 26), pal.Enemy, 1, 2);
            }
        }
    }

    // After each wave: the bonus counted up, missiles first, then cities.
    private void DrawTally(DrawingContext context, Palette pal)
    {
        DrawCentredText(context, "BONUS POINTS", 30, 250, pal.Friendly);
        DrawRightText(context, (_engine.TalliedMissiles * GameEngine.MissileBonus * _engine.Multiplier).ToString(), 22, 400, 310, Colors.White);
        for (int i = 0; i < _engine.TalliedMissiles; i++)
        {
            double x = 420 + i * 10, y = 330;
            DrawShape(context, new[] { new Point(x - 3.5, y), new Point(x, y - 9), new Point(x + 3.5, y) }, pal.Friendly, 1, pal.Friendly, 0.6, 1);
        }
        DrawRightText(context, (_engine.TalliedCities * GameEngine.CityBonus * _engine.Multiplier).ToString(), 22, 400, 360, Colors.White);
        for (int i = 0; i < _engine.TalliedCities; i++)
        {
            double x = 440 + i * 46;
            foreach (var (dx, h) in new[] { (-14.0, 12.0), (-6.0, 20.0), (2.0, 16.0), (10.0, 10.0) })
                context.FillRectangle(GetBrush(pal.City, 1), new Rect(x + dx, 386 - h, 6, h));
        }
        if (!string.IsNullOrEmpty(_engine.TallyNote))
            DrawCentredText(context, _engine.TallyNote, 26, 430, pal.Enemy);
    }

    // THE END: a great white flash spreading over everything, and the words.
    private void DrawTheEnd(DrawingContext context)
    {
        double t = Math.Max(0, _engine.EndClock);
        var cycle = new[] { Colors.White, Color.FromRgb(255, 60, 50), Color.FromRgb(255, 230, 60), Color.FromRgb(60, 120, 255), Color.FromRgb(90, 255, 120) };
        if (t < 2.2)
        {
            // Rings of an octagonal blast racing out from the middle, each flickering its own colour.
            for (int k = 5; k >= 0; k--)
            {
                double r = t * 420 - k * 55;
                if (r <= 0) continue;
                var octagon = Enumerable.Range(0, 8)
                    .Select(i => new Point(ScreenWidth / 2 + Math.Cos(i * Math.PI / 4 + Math.PI / 8) * r, 400 + Math.Sin(i * Math.PI / 4 + Math.PI / 8) * r))
                    .ToArray();
                var color = cycle[((int)(t * 18) + k) % cycle.Length];
                DrawShape(context, octagon, color, k == 0 ? 0.95 : 0.85, Colors.White, 0.8, 2);
            }
        }
        else
        {
            context.FillRectangle(GetBrush(cycle[(int)(t * 2) % 2 == 0 ? 1 : 0], 0.9), new Rect(0, 0, ScreenWidth, ScreenHeight));
        }
        DrawCentredText(context, "THE END", 110, 300, t > 2.2 && (int)(t * 2) % 2 == 0 ? Colors.White : Colors.Black, alpha: Math.Min(1, t));
        if (t > 2.2)
        {
            DrawCentredText(context, $"SCORE {_engine.Score}   WAVE {_engine.Wave}", 22, 450, Colors.White);
            if (_engine.IsEntryPending)
                DrawCentredText(context, NewEntryHeadline, 24, 490, Gold);
            else if (Blink(2))
                DrawCentredText(context, "Press SPACE or click to play again", 22, 500, Colors.White);
        }
    }
}
