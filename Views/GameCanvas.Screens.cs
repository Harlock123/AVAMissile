using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVAMissile.Engine;
using AVAMissile.Models;

namespace AVAMissile.Views;

// The score across the top, and the title, entry and high-score screens.
public partial class GameCanvas
{
    private void DrawHud(DrawingContext context)
    {
        if (_engine.State == GameState.EnterInitials) return;
        var pal = _engine.Palette;
        context.DrawText(Text(_engine.Score.ToString(), 26, pal.Enemy), new Point(250, 8));
        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        DrawCentredText(context, high.ToString(), 20, 12, pal.Friendly);
        if (_engine.BonusCities > 0 && _engine.IsInPlay)
            DrawRightText(context, $"BONUS CITIES {_engine.BonusCities}", 13, ScreenWidth - 30, 14, pal.City);
        if (_engine.Sound.IsMuted)
            DrawRightText(context, "MUTED", 11, ScreenWidth - 30, 34, Colors.White);
    }

    // ----- Title screen -----

    private void DrawTitleScreen(DrawingContext context, bool withText)
    {
        DrawBattle(context);
        if (!withText) return;

        context.FillRectangle(GetBrush(Colors.Black, 0.55), new Rect(0, 0, ScreenWidth, Field.Ground - 40));
        DrawCentredText(context, "AVAMISSILE", 82, 30, Color.FromRgb(255, 60, 50));
        DrawCentredText(context, "MISSILE COMMAND", 22, 128, Color.FromRgb(70, 120, 255));

        string[] scoring =
        {
            "MISSILE  25        SMART BOMB  125",
            "BOMBER OR SATELLITE  100",
            "BONUS: 5 PER MISSILE LEFT, 100 PER CITY",
            "POINTS MULTIPLY EVERY TWO WAVES, UP TO 6X",
            $"BONUS CITY EVERY {GameEngine.BonusCityEvery}"
        };
        for (int i = 0; i < scoring.Length; i++)
            DrawCentredText(context, scoring[i], 16, 190 + i * 26, Colors.White);

        var controls = new (string Keys, string Action)[]
        {
            ("Mouse", "Move the crosshair"),
            ("Left / middle / right click", "Fire from ALPHA / DELTA / OMEGA"),
            ("A S D  or  Z X C", "Fire from the left / centre / right base"),
            ("SPACE", "Fire from the best-placed base"),
            ("Arrows / gamepad stick", "Move the crosshair"),
            ("ESC  M", "Pause, mute")
        };
        for (int i = 0; i < controls.Length; i++)
        {
            double y = 350 + i * 22;
            context.DrawText(Text(controls[i].Keys, 14, Colors.White), new Point(150, y));
            context.DrawText(Text(controls[i].Action, 14, TextColor), new Point(430, y));
        }
        DrawCentredText(context, "The centre base, DELTA, fires its missiles much faster.", 14, 492, Gold);

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 18, 540, Gold);
        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to start", 26, 580, Colors.White);
    }

    // ----- High scores -----

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Gold);
        DrawCentredText(context, $"SCORE {_engine.Score}    WAVE {_engine.Wave}", 22, 200, TextColor);
        DrawCentredText(context, "ENTER YOUR INITIALS, COMMANDER", 24, 260, Colors.White);

        bool blinkOn = Blink(3);
        const double cell = 100;
        double left = (ScreenWidth - cell * HighScoreTable.InitialsLength) / 2;
        string initials = entry.Initials;

        for (int i = 0; i < initials.Length; i++)
        {
            bool current = i == entry.Cursor;
            char c = initials[i];
            var letter = Text(c == ' ' ? "_" : c.ToString(), 76, current ? Colors.White : Gold);
            double x = left + i * cell + (cell - letter.Width) / 2;
            context.DrawText(letter, new Point(x, 315));

            if (current)
            {
                var up = Text("^", 22, TextColor);
                var down = Text("v", 22, TextColor);
                context.DrawText(up, new Point(left + i * cell + (cell - up.Width) / 2, 300));
                context.DrawText(down, new Point(left + i * cell + (cell - down.Width) / 2, 418));
                if (blinkOn)
                    DrawGlowLine(context, new Point(left + i * cell + 18, 412), new Point(left + (i + 1) * cell - 18, 412), Colors.White, 1, 2.5);
            }
        }

        string[] help =
        {
            "Type your initials, or UP/DOWN to choose a letter",
            "and LEFT/RIGHT to move.  ENTER when done.",
            "Gamepad: D-pad to choose, X to accept, START when done",
            "Mouse: wheel to choose, click to accept"
        };
        double y = 480;
        foreach (var line in help)
        {
            DrawCentredText(context, line, 16, y, TextColor);
            y += 26;
        }
    }

    private void DrawHighScoreTable(DrawingContext context)
    {
        DrawPanel(context, new Rect(192, 60, 640, 640));
        DrawCentredText(context, "HIGH SCORES", 44, 80, Color.FromRgb(255, 60, 50));

        const double rankX = 250, nameX = 340, scoreRight = 620, waveX = 670;
        double y = 165;
        context.DrawText(Text("RANK", 16, TextColor), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, TextColor), new Point(nameX, y));
        DrawRightText(context, "SCORE", 16, scoreRight, y, TextColor);
        context.DrawText(Text("WAVE", 16, TextColor), new Point(waveX, y));

        bool blinkOn = Blink(3);
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Gold) : i == 0 ? Gold : Colors.White;

            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string wave = i < entries.Count ? entries[i].Wave.ToString() : "-";

            context.DrawText(Text($"{i + 1}.", 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            DrawRightText(context, score, 24, scoreRight, y, color);
            DrawRightText(context, wave, 24, waveX + 50, y, color);

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to play", 24, 650, Colors.White);
    }
}
