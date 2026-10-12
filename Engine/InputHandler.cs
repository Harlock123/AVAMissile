using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;

namespace AVAMissile.Engine;

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
        Stick.Length > 0.01 || IsFiringAny || IsPausePressed || IsBackPressed;

    /// <summary>Moves the crosshair from the keyboard or gamepad, like a trackball rolled steadily. Down is positive Y.</summary>
    public Vector Stick
    {
        get
        {
            double x = 0, y = 0;
            if (IsKeyPressed(Key.Left) || _controller.DPadLeft) x -= 1;
            if (IsKeyPressed(Key.Right) || _controller.DPadRight) x += 1;
            if (IsKeyPressed(Key.Up) || _controller.DPadUp) y -= 1;
            if (IsKeyPressed(Key.Down) || _controller.DPadDown) y += 1;
            var key = new Vector(x, y);
            if (key.Length > 0) return key.Length > 1 ? key / key.Length : key;
            var pad = new Vector(_controller.LeftStickX, _controller.LeftStickY);
            return pad.Length < 0.15 ? default : pad;
        }
    }

    /// <summary>Where the mouse pointer is on the game's 1024×768 screen, and whether it has moved since last asked.</summary>
    public Point Pointer { get; private set; }
    private bool _pointerMoved;

    public void OnPointer(Point screen)
    {
        Pointer = screen;
        _pointerMoved = true;
    }

    public bool TakePointerMove()
    {
        bool moved = _pointerMoved;
        _pointerMoved = false;
        return moved;
    }

    // The cabinet's three fire buttons, one for each base: left, centre and right.
    public bool IsFiringLeft => IsKeyPressed(Key.A) || IsKeyPressed(Key.Z) || _controller.ButtonX || _controller.LeftBumper || _leftMousePressed;
    public bool IsFiringCentre => IsKeyPressed(Key.S) || IsKeyPressed(Key.X) || _controller.ButtonA || _controller.ButtonY || _middleMousePressed;
    public bool IsFiringRight => IsKeyPressed(Key.D) || IsKeyPressed(Key.C) || _controller.ButtonB || _controller.RightBumper || _rightMousePressed;

    /// <summary>Fire from whichever base is best placed (Space, or a trigger).</summary>
    public bool IsFiringAuto => IsKeyPressed(Key.Space) || IsKeyPressed(Key.LeftCtrl) || IsKeyPressed(Key.RightCtrl) ||
                                _controller.RightTrigger > TriggerThreshold || _controller.LeftTrigger > TriggerThreshold;

    public bool IsFiringAny => IsFiringLeft || IsFiringCentre || IsFiringRight || IsFiringAuto;

    public bool IsPausePressed =>
        _controller.ButtonStart;

    public bool IsBackPressed =>
        _controller.ButtonBack;

    public void Dispose()
    {
        _controller.Dispose();
    }
}
