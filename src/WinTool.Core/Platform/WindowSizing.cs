using System;

namespace WinTool.Core.Platform
{
    /// <summary>The window size to open with, in device-independent pixels.</summary>
    public struct WindowPlacement
    {
        public WindowPlacement(double width, double height, bool maximize)
        {
            Width = width;
            Height = height;
            Maximize = maximize;
        }

        public double Width { get; }
        public double Height { get; }
        public bool Maximize { get; }
    }

    /// <summary>
    /// Opening size rules (decision D-019). The app must work on a freshly
    /// installed computer at 1024x768, and on 1366x768 laptops at 125% scaling
    /// (about 1093x582 usable). Sizes are in DIPs, so scaling is already included.
    /// </summary>
    public static class WindowSizing
    {
        public const double PreferredWidth = 1280;
        public const double PreferredHeight = 800;

        /// <summary>Smallest size at which every screen still works (content scrolls).</summary>
        public const double MinimumWidth = 760;
        public const double MinimumHeight = 520;

        /// <summary>Below this width the sidebar shows icons only.</summary>
        public const double CompactSidebarBelowWidth = 1100;

        private const double Margin = 40;

        public static WindowPlacement Choose(double workAreaWidth, double workAreaHeight)
        {
            if (workAreaWidth <= 0 || workAreaHeight <= 0)
            {
                return new WindowPlacement(PreferredWidth, PreferredHeight, false);
            }

            if (workAreaWidth >= PreferredWidth + Margin && workAreaHeight >= PreferredHeight + Margin)
            {
                return new WindowPlacement(PreferredWidth, PreferredHeight, false);
            }

            // Small screens: use all of it. A window with a margin around it only wastes space there.
            if (workAreaWidth < CompactSidebarBelowWidth || workAreaHeight < 720)
            {
                return new WindowPlacement(Math.Max(MinimumWidth, workAreaWidth), Math.Max(MinimumHeight, workAreaHeight), true);
            }

            return new WindowPlacement(
                Math.Min(PreferredWidth, workAreaWidth - Margin),
                Math.Min(PreferredHeight, workAreaHeight - Margin),
                false);
        }

        public static bool UseCompactSidebar(double windowWidth)
        {
            return windowWidth < CompactSidebarBelowWidth;
        }
    }
}
