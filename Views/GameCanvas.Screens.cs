using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVASinstar.Engine;
using AVASinstar.Models;

namespace AVASinstar.Views;

// The readouts and scanner across the top, and the title, entry and high-score screens.
public partial class GameCanvas
{
    private static readonly Rect ScannerRect = new(352, 10, 320, 120);
    private const double ScannerRange = 4400;   // world units across the scanner

    private void DrawHud(DrawingContext context)
    {
        if (_engine.State == GameState.EnterInitials) return;

        context.FillRectangle(GetBrush(Colors.Black, 0.55), new Rect(0, 0, ScreenWidth, 152));
        DrawGlowLine(context, new Point(0, 152), new Point(ScreenWidth, 152), FrameColor, 0.5, 1);

        // Score and ships, top left.
        context.DrawText(Text("SCORE", 13, TextColor, 0.8), new Point(22, 12));
        context.DrawText(Text(_engine.Score.ToString(), 28, Colors.White), new Point(22, 26));
        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        context.DrawText(Text($"HIGH {high}", 13, Gold), new Point(22, 64));
        int spare = Math.Max(0, _engine.Lives - (_engine.ShipAlive ? 1 : 0));
        for (int i = 0; i < spare && i < 8; i++)
            DrawShape(context, Place(ShipShape, new Point(34 + i * 30, 104), -Math.PI / 2, 0.75), Color.FromRgb(120, 125, 140), 1, GameEngine.ShipColor, 0.9, 1.2);

        // Sinibombs and the zone, top right.
        DrawRightText(context, _engine.ZoneName, 15, ScreenWidth - 22, 12, Gold);
        DrawRightText(context, $"SINIBOMBS {_engine.Ship.Bombs}", 15, ScreenWidth - 22, 38, GameEngine.BombColor);
        for (int i = 0; i < _engine.Ship.Bombs; i++)
        {
            var p = new Point(ScreenWidth - 30 - (i % 10) * 15, 72 + (i / 10) * 15);
            context.DrawEllipse(GetBrush(GameEngine.BombColor, 0.4), null, p, 6, 6);
            context.DrawEllipse(GetBrush(Colors.White, 0.9), null, p, 2.5, 2.5);
        }
        if (_engine.Sound.IsMuted || !_engine.Sound.IsVoiceOn)
            DrawRightText(context, (_engine.Sound.IsMuted ? "MUTED " : "") + (!_engine.Sound.IsVoiceOn ? "VOICE OFF" : ""), 11, ScreenWidth - 22, 112, TextColor);

        DrawScanner(context);
        DrawSinistarGauge(context);

        if (_engine.BannerTime > 0 && _engine.Banner != null && _engine.State != GameState.GameOver)
        {
            double alpha = Math.Min(1, _engine.BannerTime * 2);
            bool sinistar = _engine.Banner.StartsWith("BEWARE") || _engine.Banner.StartsWith("SINISTAR");
            DrawCentredText(context, _engine.Banner, sinistar ? 42 : 32, 220, sinistar ? AlertRed : Gold, alpha: alpha);
            if (_engine.SubBanner != null)
                DrawCentredText(context, _engine.SubBanner, 18, 272, TextColor, alpha: alpha);
        }
    }

    // The long-range scanner: the sector round the ship, with the Sinistar flashing once it lives.
    private void DrawScanner(DrawingContext context)
    {
        var r = ScannerRect;
        context.FillRectangle(GetBrush(Color.FromRgb(0, 10, 20), 0.9), r);
        var centre = r.Center;
        double scale = r.Width / ScannerRange;

        Point? Map(Point world)
        {
            var d = World.Delta(_engine.Ship.Position, world);
            var p = centre + d * scale;
            return r.Contains(p) ? p : null;
        }

        void Dot(Point world, Color color, double size, double alpha = 1)
        {
            if (Map(world) is { } p)
                context.FillRectangle(GetBrush(color, alpha), new Rect(p.X - size / 2, p.Y - size / 2, size, size));
        }

        foreach (var rock in _engine.Planetoids) Dot(rock.Position, GameEngine.RockColor, 3, 0.8);
        foreach (var c in _engine.Crystals) Dot(c.Position, GameEngine.CrystalColor, 1.5);
        foreach (var w in _engine.Workers) Dot(w.Position, GameEngine.WorkerColor, 2.5);
        foreach (var w in _engine.Warriors) Dot(w.Position, GameEngine.WarriorColor, 3);
        foreach (var b in _engine.Bombs) Dot(b.Position, GameEngine.BombColor, 2.5);

        var s = _engine.Sinistar;
        if (s.PiecesBuilt > 0)
        {
            bool show = !s.IsAwake || Blink(4);
            if (show && Map(s.Position) is { } sp)
            {
                double size = 5 + 5 * s.PiecesBuilt / (double)Sinistar.Pieces;
                context.DrawEllipse(GetBrush(s.IsAwake ? AlertRed : GameEngine.SinistarColor, 0.95), null, sp, size, size);
            }
        }

        if (_engine.ShipAlive)
            context.FillRectangle(GetBrush(Colors.White, 1), new Rect(centre.X - 2, centre.Y - 2, 4, 4));
        DrawFrame(context, r, FrameColor, 0.8);
    }

    // How much of the Sinistar is built: a row of twenty cells under the scanner.
    private void DrawSinistarGauge(DrawingContext context)
    {
        var s = _engine.Sinistar;
        double cell = 13, gap = 3, total = Sinistar.Pieces * (cell + gap) - gap;
        double x0 = (ScreenWidth - total) / 2, y = 136;
        var color = s.IsAwake ? AlertRed : GameEngine.SinistarColor;
        context.DrawText(Text(s.IsAwake ? "SINISTAR LIVES" : "SINISTAR", 11, color), new Point(x0 - 112, y - 3));
        for (int i = 0; i < Sinistar.Pieces; i++)
        {
            var rect = new Rect(x0 + i * (cell + gap), y, cell, 8);
            if (i < s.PiecesBuilt)
                context.FillRectangle(GetBrush(color, s.IsAwake && Blink(4) ? 0.5 : 0.95), rect);
            else
                context.DrawRectangle(GetPen(color, 0.3, 1), rect);
        }
        if (s.IsAwake)
            context.DrawText(Text($"{Sinistar.BombsToKill - s.BombHits} TO GO", 11, color), new Point(x0 + total + 10, y - 3));
    }

    // ----- Title screen -----

    private void DrawTitleScreen(DrawingContext context, bool withText)
    {
        DrawSpace(context);
        if (!withText) return;

        context.FillRectangle(GetBrush(Colors.Black, 0.6), new Rect(0, 0, ScreenWidth, ScreenHeight));
        DrawCentredText(context, "AVASINSTAR", 76, 14, AlertRed);
        DrawCentredText(context, "BEWARE, I LIVE!", 20, 104, Gold);

        // The face itself, complete and grinning.
        DrawTitleFace(context, new Point(ScreenWidth / 2, 262));

        var legend = new (string Name, string Value, Action<Point> Draw)[]
        {
            ("CRYSTAL", $"{GameEngine.CrystalPoints}", p => DrawCrystal(context, p, Clock)),
            ("WORKER", $"{Worker.Points}", p => DrawShape(context, Place(WorkerBody, p, Clock), Color.FromRgb(170, 35, 30), 1, GameEngine.WorkerColor, 1, 1.4)),
            ("WARRIOR", $"{Warrior.Points}", p => DrawShape(context, Place(WarriorShape, p, Clock), Color.FromRgb(20, 70, 110), 1, GameEngine.WarriorColor, 1, 1.5)),
            ("SINISTAR PIECE", $"{Sinistar.PiecePoints}", p => DrawShape(context, Sinistar.PieceShapes[16].Select(q => p + new Vector(q.X, q.Y + 20)).ToArray(), Color.FromRgb(120, 120, 128), 1, GameEngine.SinistarColor, 1, 1.4)),
            ("SINISTAR", $"{Sinistar.Points}", p => { context.DrawEllipse(GetBrush(AlertRed, 0.8), null, p + new Vector(-8, 0), 4, 4); context.DrawEllipse(GetBrush(AlertRed, 0.8), null, p + new Vector(8, 0), 4, 4); })
        };
        for (int i = 0; i < legend.Length; i++)
        {
            double cx = 112 + i * 200;
            legend[i].Draw(new Point(cx, 430));
            DrawCentredText(context, legend[i].Name, 13, 456, TextColor, cx);
            DrawCentredText(context, legend[i].Value, 15, 474, Colors.White, cx);
        }

        var controls = new (string Keys, string Action)[]
        {
            ("Arrows / WASD", "Fly (8 ways)"),
            ("Mouse", "Fly toward the pointer"),
            ("SPACE, CTRL, left click", "Fire"),
            ("SHIFT, E, right click", "Sinibomb"),
            ("Gamepad", "Stick flies, A fires, B bombs"),
            ("ESC  M  V", "Pause, mute, voice")
        };
        string[] orders =
        {
            "Shoot planetoids to knock crystals loose,",
            "and collect them: each is a Sinibomb.",
            "Workers carry crystals off to build the",
            "Sinistar. When it's whole, it lives,",
            "and it hunts you. Thirteen Sinibombs",
            "destroy it. Lasers only bounce off."
        };
        for (int i = 0; i < orders.Length; i++)
        {
            double y = 512 + i * 20;
            context.DrawText(Text(controls[i].Keys, 14, Colors.White), new Point(40, y));
            context.DrawText(Text(controls[i].Action, 14, TextColor), new Point(252, y));
            context.DrawText(Text(orders[i], 14, TextColor), new Point(580, y));
        }

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 18, 680, Gold);
        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to start", 26, 712, Colors.White);
    }

    private void DrawTitleFace(DrawingContext context, Point at)
    {
        double jaw = (0.5 + 0.5 * Math.Sin(Clock * 3)) * 14;
        DrawShape(context, new[] { at + new Vector(-40, 50), at + new Vector(40, 50), at + new Vector(40, 56 + jaw), at + new Vector(-40, 56 + jaw) },
            Color.FromRgb(20, 0, 0), 1, Color.FromRgb(120, 0, 0), 0.8, 1);
        for (int i = 0; i < Sinistar.Pieces; i++)
        {
            var offset = i == Sinistar.JawPiece ? new Vector(0, jaw) : default;
            var placed = Sinistar.PieceShapes[i].Select(p => at + new Vector(p.X, p.Y) + offset).ToArray();
            DrawShape(context, placed, i < 12 ? Color.FromRgb(96, 96, 104) : Color.FromRgb(120, 120, 128), 1, GameEngine.SinistarColor, 1, 1.4);
        }
        for (int t = -36; t <= 36; t += 12)
            DrawGlowLine(context, at + new Vector(t, 38), at + new Vector(t, 50), Color.FromRgb(40, 40, 44), 1, 1.5);
        foreach (var e in Sinistar.Eyes)
        {
            var eye = at + new Vector(e.X, e.Y);
            double glow = 0.75 + 0.25 * Math.Sin(Clock * 6);
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 30, 20), 0.3 * glow), null, eye, 18, 15);
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 60, 40), glow), null, eye, 9, 7);
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 220, 180), glow), null, eye, 3, 2.5);
        }
    }

    // ----- High scores -----

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Gold);
        DrawCentredText(context, $"SCORE {_engine.Score}    ZONE {_engine.Zone}", 22, 200, TextColor);
        DrawCentredText(context, "ENTER YOUR INITIALS", 24, 260, Colors.White);

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
        DrawCentredText(context, "HIGH SCORES", 44, 80, AlertRed);

        const double rankX = 250, nameX = 340, scoreRight = 630, zoneX = 680;
        double y = 165;
        context.DrawText(Text("RANK", 16, TextColor), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, TextColor), new Point(nameX, y));
        DrawRightText(context, "SCORE", 16, scoreRight, y, TextColor);
        context.DrawText(Text("ZONE", 16, TextColor), new Point(zoneX, y));

        bool blinkOn = Blink(3);
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Gold) : i == 0 ? Gold : Colors.White;

            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string zone = i < entries.Count && entries[i].Zone > 0 ? entries[i].Zone.ToString() : "-";

            context.DrawText(Text($"{i + 1}.", 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            DrawRightText(context, score, 24, scoreRight, y, color);
            context.DrawText(Text(zone, 24, color), new Point(zoneX + 14, y));

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to play", 24, 650, Colors.White);
    }
}
