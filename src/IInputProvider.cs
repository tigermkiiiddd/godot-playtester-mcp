#nullable disable
using Godot;
namespace GodotPlaytester;


/// <summary>虚拟鼠标状态源：游戏若需与模拟输入合流，统一经此读取（双管线约定见源库 docs/integration.md）。</summary>
public interface IInputProvider
{
    Vector2 MousePosition { get; }
    bool MouseLeftDown { get; }
    bool MouseLeftJustPressed { get; }
    bool MouseLeftJustReleased { get; }
}
