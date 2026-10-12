using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVASinstar.Engine;
using AVASinstar.Models;

namespace AVASinstar.Views;

/// <summary>
/// The view of space round the ship, with the scanner and readouts, drawn on a fixed 1024×768 screen
/// that is scaled to fit the window. The ship stays in the middle; space scrolls past it.
/// </summary>
public partial class GameCanvas : Control
{
    private const double ScreenWidth = 1024;
    private const double ScreenHeight = 768;
    private static readonly Point ScreenCentre = new(ScreenWidth / 2, ScreenHeight / 2 + 30);

    private readonly GameEngine _engine;
    private readonly DispatcherTimer _gameTimer;
    private DateTime _lastUpdate;
    private bool _wasPausePressed;
    private bool _wasBackPressed;
    private bool _cursorHidden;

    public GameCanvas()
    {
        _engine = new GameEngine();
        _lastUpdate = DateTime.Now;

        _gameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _gameTimer.Tick += GameLoop;

        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _gameTimer.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _gameTimer.Stop();
        _engine.Dispose();
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        // Handle Ctrl+S (or Cmd+S on macOS) for screenshot
        if (e.Key == Avalonia.Input.Key.S &&
            (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) ||
             e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)))
        {
            TakeScreenshot();
            e.Handled = true;
            return;
        }

        // While entering initials every key belongs to the entry (M is a letter, not mute).
        if (_engine.State == GameState.EnterInitials)
        {
            _engine.InitialsKey(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.M)
        {
            _engine.Sound.IsMuted = !_engine.Sound.IsMuted;
            if (_engine.Sound.IsMuted)
            {
                _engine.Sound.StopAllLoops();
                _engine.Sound.ClearVoice();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.V)
        {
            _engine.Sound.IsVoiceOn = !_engine.Sound.IsVoiceOn;
            if (!_engine.Sound.IsVoiceOn)
                _engine.Sound.ClearVoice();
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.Escape)
        {
            if (_engine.State == GameState.Playing)
            {
                _engine.Pause();
            }
            else if (_engine.State is GameState.Attract or GameState.HighScores)
            {
                _engine.ReturnToTitle();
            }
            else
            {
                var window = TopLevel.GetTopLevel(this) as Window;
                window?.Close();
            }
            e.Handled = true;
            return;
        }

        if (_engine.State == GameState.Paused)
        {
            _engine.Resume();
            e.Handled = true;
            return;
        }

        _engine.Input.OnKeyDown(e.Key);
        e.Handled = true;
    }

    protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
    {
        _engine.Input.OnKeyUp(e.Key);
        e.Handled = true;
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Left);
        if (props.IsRightButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Right);
        if (props.IsMiddleButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Middle);
        e.Handled = true;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Left);
        if (!props.IsRightButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Right);
        if (!props.IsMiddleButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Middle);
        e.Handled = true;
    }

    // With the mouse, the ship flies toward the pointer: further from the ship, faster.
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        var (scale, offsetX, offsetY) = Fit();
        if (scale <= 0) return;
        var p = e.GetPosition(this);
        var screen = new Point((p.X - offsetX) / scale, (p.Y - offsetY) / scale);
        _engine.Input.OnPointer(screen - ScreenCentre);
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        _engine.Input.OnMouseWheel(e.Delta.Y);
        e.Handled = true;
    }

    private void TakeScreenshot()
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            return;

        using var bitmap = new RenderTargetBitmap(pixelSize);
        bitmap.Render(this);

        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var filename = Path.Combine(picturesPath, $"AVASinstar_{timestamp}.png");

        bitmap.Save(filename, PngBitmapEncoderOptions.Default);
    }

    private void GameLoop(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double deltaTime = (now - _lastUpdate).TotalSeconds;
        _lastUpdate = now;

        deltaTime = Math.Min(deltaTime, 0.1);

        // Poll controller state
        _engine.Input.Update();

        // Handle controller pause/resume (with edge detection)
        HandleControllerPause();

        _engine.Update(deltaTime);
        UpdateCursor();
        _engine.Input.PostUpdate();
        InvalidateVisual();
    }

    // While flying, the pointer is a small crosshair over space rather than an arrow.
    private void UpdateCursor()
    {
        bool hide = _engine.State == GameState.Playing;
        if (hide == _cursorHidden) return;
        _cursorHidden = hide;
        Cursor = hide ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross) : null;
    }

    private void HandleControllerPause()
    {
        // Start button toggles pause (edge detection)
        bool pausePressed = _engine.Input.IsPausePressed;
        if (pausePressed && !_wasPausePressed)
        {
            if (_engine.State == GameState.Playing)
                _engine.Pause();
            else if (_engine.State == GameState.Paused)
                _engine.Resume();
        }
        _wasPausePressed = pausePressed;

        // Back button exits when paused (edge detection)
        bool backPressed = _engine.Input.IsBackPressed;
        if (backPressed && !_wasBackPressed && _engine.State == GameState.Paused)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            window?.Close();
        }
        _wasBackPressed = backPressed;
    }

    /// <summary>How the fixed 4:3 screen fits in the window: scale, and the letterbox offsets.</summary>
    private (double Scale, double OffsetX, double OffsetY) Fit()
    {
        double scale = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (scale, (Bounds.Width - ScreenWidth * scale) / 2, (Bounds.Height - ScreenHeight * scale) / 2);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));

        var (scale, offsetX, offsetY) = Fit();
        if (scale <= 0) return;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        using (context.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
        {
            switch (_engine.State)
            {
                case GameState.StartScreen:
                    DrawTitleScreen(context, withText: true);
                    break;

                case GameState.HighScores:
                    DrawTitleScreen(context, withText: false);
                    DrawHighScoreTable(context);
                    break;

                default:
                    DrawCockpitView(context);
                    break;
            }
        }
    }

    private void DrawCockpitView(DrawingContext context)
    {
        DrawSpace(context);
        DrawHud(context);

        switch (_engine.State)
        {
            case GameState.Paused:
                DrawPanel(context, new Rect(262, 330, 500, 150));
                DrawCentredText(context, "PAUSED", 46, 350, Colors.White);
                DrawCentredText(context, "ESC to quit, any other key to resume", 18, 420, TextColor);
                break;

            case GameState.GameOver:
                DrawPanel(context, new Rect(222, 320, 580, _engine.IsEntryPending ? 170 : 200));
                DrawCentredText(context, "GAME OVER", 52, 335, AlertRed);
                DrawCentredText(context, $"SCORE {_engine.Score}   ZONE {_engine.Zone}", 18, 405, TextColor);
                if (_engine.IsEntryPending)
                    DrawCentredText(context, NewEntryHeadline, 22, 440, Gold);
                else if (Blink(2))
                    DrawCentredText(context, "Press SPACE or click to play again", 22, 460, Colors.White);
                break;

            case GameState.EnterInitials:
                DrawInitialsEntry(context);
                break;

            case GameState.Attract:
                DrawCentredText(context, "DEMONSTRATION", 18, 190, Gold);
                if (Blink(2))
                    DrawCentredText(context, "Press SPACE or click to play", 20, ScreenHeight - 44, Colors.White);
                break;
        }
    }
}
