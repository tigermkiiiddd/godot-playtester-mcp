using Godot;
namespace GodotPlaytester;
using System.Collections.Generic;


public enum MacroStepType
{
    HoldKey,
    TapKey,
    RepeatKey,
    ComboKeys,
    MoveDistance,
    MoveTo,
    Click,
    Wait,
    Drag,
    DoubleClick,
    TypeText
}

public class MacroStep
{
    public MacroStepType Type;
    public string Label = "";

    public string[] Keys = System.Array.Empty<string>();
    public Key[] KeyCodes = System.Array.Empty<Key>();

    public double Duration = 0.5;
    public int Count = 1;
    public double Interval = 0.5;

    public string Direction = "";
    public float Distance = 0f;

    public float TargetX, TargetY;
    public string Mode = "8dir"; // "8dir" diagonal, "4dir" single-axis L-path, "free" any-angle smooth

    public float X, Y;
    public string Button = "left";
    public MouseButton ButtonIndex; // runtime: resolved drag button

    // TypeText fields
    public string Text = "";
    public double CharInterval = 0.05;
    public int CharIndex;
    public double CharTimer;

    // Runtime state
    public string Status = "pending";
    public double Elapsed;
    public int RepeatIndex;
    public double RepeatTimer;
    public bool KeyPressed;
    public Vector2 StartPosition;
    public string ErrorMessage;
    public Vector2 LastCheckPos;
    public double StuckTimer;
}

public class MacroRun
{
    public string Id;
    public string Name = "";
    public List<MacroStep> Steps = new();
    public int CurrentStepIndex;

    public string Status = "running";
    public double StartTime;
    public double Duration;
    public string ErrorMessage;

    public HashSet<string> HeldKeys = new();
}
