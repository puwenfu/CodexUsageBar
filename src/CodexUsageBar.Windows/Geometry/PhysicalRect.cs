namespace CodexUsageBar.Windows.Geometry;

public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
