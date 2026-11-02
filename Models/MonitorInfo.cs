namespace FreeScreenRecorder.Models;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int x, int y) =>
        x >= X && y >= Y && x < Right && y < Bottom;
}

public sealed record MonitorInfo(
    string DeviceName,
    string DisplayLabel,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary,
    int DxgiOutputIndex)
{
    public ScreenRect Bounds => new(X, Y, Width, Height);

    public override string ToString() => DisplayLabel;
}
