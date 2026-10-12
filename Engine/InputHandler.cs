using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;

namespace AVASinstar.Engine;

public class InputHandler : IDisposable
{
    private readonly HashSet<Key> _pressedKeys = new();
    private readonly ControllerManager _controller = new();

    // Threshold for analog stick to register as direction
    private const float StickThreshold = 0.3f;
    // Threshold for trigger to register as pressed
    private const float TriggerThreshold = 0.3f;

    // Mouse state
    private bool _leftMousePressed;
    private bool _rightMousePressed;
    private bool _middleMousePressed;
    private double _mouseWheelDelta;
    private double _spinnerDelta;

    public void OnKeyDown(Key key)
    {
        // A steering key hands the stick back from the mouse.
        if (key is Key.A or Key.D or Key.W or Key.S or Key.Left or Key.Right or Key.Up or Key.Down)
            _mouseActive = false;
        _pressedKeys.Add(key);
    }

    public void OnKeyUp(Key key)
    {
        _pressedKeys.Remove(key);
    }

    public bool IsKeyPressed(Key key)
    {
        return _pressedKeys.Contains(key);
    }

    public void OnMouseDown(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = true;
        else if (button == MouseButton.Right)
            _rightMousePressed = true;
        else if (button == MouseButton.Middle)
            _middleMousePressed = true;
    }

    public void OnMouseUp(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = false;
        else if (button == MouseButton.Right)
            _rightMousePressed = false;
        else if (button == MouseButton.Middle)
            _middleMousePressed = false;
    }

    public void OnMouseWheel(double delta)
    {
        _mouseWheelDelta += delta;
    }

    /// <summary>Sideways mouse movement, in pixels, for spinner mode.</summary>
    public void OnSpinner(double dx)
    {
        _spinnerDelta += dx;
    }

    public void Update()
    {
        _controller.Update();
    }

    public void PostUpdate()
    {
        // Reset mouse wheel delta after each frame
        _mouseWheelDelta = 0;
        _spinnerDelta = 0;
    }

    public bool IsLeftMousePressed => _leftMousePressed;
    public bool IsRightMousePressed => _rightMousePressed;
    public double MouseWheelDelta => _mouseWheelDelta;
    public double SpinnerDelta => _spinnerDelta;

    public bool IsControllerConnected => _controller.IsConnected;

    // Controller-only menu inputs, for initials entry (the keyboard is handled key by key there).
    public bool PadUp => _controller.DPadUp || _controller.LeftStickY < -0.5f;
    public bool PadDown => _controller.DPadDown || _controller.LeftStickY > 0.5f;
    public bool PadLeft => _controller.DPadLeft || _controller.LeftStickX < -0.5f;
    public bool PadRight => _controller.DPadRight || _controller.LeftStickX > 0.5f;
    public bool PadAccept => _controller.ButtonX || _controller.ButtonB || _controller.ButtonA;
    public bool PadDone => _controller.ButtonStart;

    /// <summary>Any key, mouse button, wheel turn or controller input at all this frame.</summary>
    public bool IsAnyInput =>
        _pressedKeys.Count > 0 ||
        _leftMousePressed || _rightMousePressed || _middleMousePressed || _mouseWheelDelta != 0 ||
        KeyStick.Length > 0.01 || PadStick.Length > 0.01 || IsFiring || IsBombing ||
        IsPausePressed || IsBackPressed;

    /// <summary>
    /// The flight stick, as the cabinet's 49-way joystick: a direction and how far it's pushed (length 0 to 1,
    /// screen-up negative Y). The keyboard and d-pad push it all the way in eight directions; an analog
    /// stick pushes it as far as it is pushed; the mouse pushes it toward the pointer, further the further
    /// the pointer is from the middle of the screen.
    /// </summary>
    public Vector Stick
    {
        get
        {
            var key = KeyStick;
            if (key.Length > 0) return key;
            var pad = PadStick;
            if (pad.Length > 0) return pad;
            return _mouseActive ? _mouseStick : default;
        }
    }

    private Vector KeyStick
    {
        get
        {
            double x = 0, y = 0;
            if (IsKeyPressed(Key.A) || IsKeyPressed(Key.Left) || _controller.DPadLeft) x -= 1;
            if (IsKeyPressed(Key.D) || IsKeyPressed(Key.Right) || _controller.DPadRight) x += 1;
            if (IsKeyPressed(Key.W) || IsKeyPressed(Key.Up) || _controller.DPadUp) y -= 1;
            if (IsKeyPressed(Key.S) || IsKeyPressed(Key.Down) || _controller.DPadDown) y += 1;
            var v = new Vector(x, y);
            return v.Length > 1 ? v / v.Length : v;
        }
    }

    private Vector PadStick
    {
        get
        {
            var v = new Vector(_controller.LeftStickX, _controller.LeftStickY);
            if (v.Length < StickThreshold * 0.5) return default;
            return v.Length > 1 ? v / v.Length : v;
        }
    }

    // The mouse steers once it has been moved, until a key or the gamepad takes over.
    private bool _mouseActive;
    private Vector _mouseStick;

    /// <summary>The pointer's offset from the middle of the screen, scaled so the stick is fully pushed near the edge.</summary>
    public void OnPointer(Vector fromCentre)
    {
        _mouseActive = true;
        var v = fromCentre / 300;
        _mouseStick = v.Length > 1 ? v / v.Length : v;
        // A small dead zone in the middle, so the ship can sit still.
        if (_mouseStick.Length < 0.08) _mouseStick = default;
    }

    public bool IsFiring =>
        IsKeyPressed(Key.Space) || IsKeyPressed(Key.LeftCtrl) || IsKeyPressed(Key.RightCtrl) ||
        _controller.ButtonA || _controller.ButtonX || _controller.RightBumper ||
        _controller.RightTrigger > TriggerThreshold ||
        _leftMousePressed;

    /// <summary>The Sinibomb button.</summary>
    public bool IsBombing =>
        IsKeyPressed(Key.LeftShift) || IsKeyPressed(Key.RightShift) || IsKeyPressed(Key.E) ||
        IsKeyPressed(Key.F) || IsKeyPressed(Key.Z) ||
        _controller.ButtonB || _controller.ButtonY || _controller.LeftBumper ||
        _controller.LeftTrigger > TriggerThreshold ||
        _rightMousePressed || _middleMousePressed;

    public bool IsPausePressed =>
        _controller.ButtonStart;

    public bool IsBackPressed =>
        _controller.ButtonBack;

    public void Dispose()
    {
        _controller.Dispose();
    }
}
