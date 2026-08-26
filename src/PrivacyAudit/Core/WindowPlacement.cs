namespace PrivacyAudit.Core;

public readonly record struct WindowArea(double Left, double Top, double Width, double Height);
public readonly record struct WindowPosition(double Left, double Top);

/// <summary>Keeps the title bar reachable without changing the designed window size.</summary>
public static class WindowPlacement
{
    public static WindowPosition InitialPosition(double windowWidth, double windowHeight, WindowArea workArea)
    {
        var left = windowWidth <= workArea.Width
            ? workArea.Left + Math.Max(0, (workArea.Width - windowWidth) / 2)
            : workArea.Left;
        var top = windowHeight <= workArea.Height
            ? workArea.Top + Math.Max(0, (workArea.Height - windowHeight) / 2)
            : workArea.Top;
        return new(left, top);
    }
}
