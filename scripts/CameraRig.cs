using Godot;

namespace Star;

/// <summary>斜俯视相机：WASD 平移、滚轮缩放、中键拖动平移。</summary>
public partial class CameraRig : Camera3D
{
    private const float MinDistance = 18f;
    private const float MaxDistance = 72f;
    private const float PitchDegrees = -56f;

    public Vector3 Focus = Vector3.Zero;

    private float _distance = 30f;
    private bool _dragging;

    public override void _Ready()
    {
        Fov = 50f;
        UpdateTransform();
    }

    public override void _Process(double delta)
    {
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
        {
            input.Y -= 1f;
        }

        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
        {
            input.Y += 1f;
        }

        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
        {
            input.X -= 1f;
        }

        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
        {
            input.X += 1f;
        }

        if (input == Vector2.Zero)
        {
            return;
        }

        input = input.Normalized();
        Focus += new Vector3(input.X, 0f, input.Y) * (_distance * 0.9f * (float)delta);
        ClampFocus();
        UpdateTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton button:
                if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
                {
                    Zoom(-1f);
                }
                else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
                {
                    Zoom(1f);
                }
                else if (button.ButtonIndex == MouseButton.Middle)
                {
                    _dragging = button.Pressed;
                }

                break;
            case InputEventMouseMotion motion when _dragging:
                Focus += new Vector3(-motion.Relative.X, 0f, -motion.Relative.Y) * (_distance * 0.0016f);
                ClampFocus();
                UpdateTransform();
                break;
        }
    }

    private void Zoom(float direction)
    {
        _distance = Mathf.Clamp(_distance * (direction < 0f ? 0.9f : 1.1f), MinDistance, MaxDistance);
        UpdateTransform();
    }

    private void ClampFocus()
    {
        float limit = HexMap.HexSize * 12f;
        Focus = new Vector3(
            Mathf.Clamp(Focus.X, -limit, limit),
            0f,
            Mathf.Clamp(Focus.Z, -limit, limit));
    }

    private void UpdateTransform()
    {
        float pitch = Mathf.DegToRad(-PitchDegrees);
        Position = Focus + new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)) * _distance;
        LookAt(Focus, Vector3.Up);
    }
}
