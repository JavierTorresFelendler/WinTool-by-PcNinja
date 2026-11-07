using System;
using System.Windows;
using System.Windows.Controls;

namespace WinTool.App.Controls
{
    /// <summary>
    /// Two children: content (first) and actions (second). Side by side while the content still gets
    /// <see cref="MinContentWidth"/>; otherwise the actions move below the content, aligned to the end.
    /// Keeps status text readable in narrow windows and long translations (Spanish, VM feedback).
    /// </summary>
    public sealed class ResponsiveRow : Panel
    {
        public static readonly DependencyProperty MinContentWidthProperty = DependencyProperty.Register(
            nameof(MinContentWidth), typeof(double), typeof(ResponsiveRow),
            new FrameworkPropertyMetadata(280.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(ResponsiveRow),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinContentWidth { get => (double)GetValue(MinContentWidthProperty); set => SetValue(MinContentWidthProperty, value); }
        public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

        private bool stacked;

        protected override Size MeasureOverride(Size available)
        {
            if (InternalChildren.Count < 2) return MeasureSingle(available);
            UIElement content = InternalChildren[0], actions = InternalChildren[1];
            actions.Measure(new Size(double.PositiveInfinity, available.Height));
            var actionsWidth = actions.DesiredSize.Width;
            stacked = !double.IsInfinity(available.Width) && available.Width - actionsWidth - Spacing < MinContentWidth;
            if (stacked)
            {
                content.Measure(new Size(available.Width, double.PositiveInfinity));
                actions.Measure(new Size(available.Width, double.PositiveInfinity));
                return new Size(Math.Max(content.DesiredSize.Width, actions.DesiredSize.Width),
                    content.DesiredSize.Height + Spacing + actions.DesiredSize.Height);
            }

            var contentWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width - actionsWidth - Spacing;
            content.Measure(new Size(contentWidth, available.Height));
            return new Size(content.DesiredSize.Width + Spacing + actionsWidth, Math.Max(content.DesiredSize.Height, actions.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (InternalChildren.Count < 2)
            {
                if (InternalChildren.Count == 1) InternalChildren[0].Arrange(new Rect(final));
                return final;
            }

            UIElement content = InternalChildren[0], actions = InternalChildren[1];
            if (stacked)
            {
                content.Arrange(new Rect(0, 0, final.Width, content.DesiredSize.Height));
                var actionsWidth = Math.Min(actions.DesiredSize.Width, final.Width);
                actions.Arrange(new Rect(final.Width - actionsWidth, content.DesiredSize.Height + Spacing, actionsWidth, actions.DesiredSize.Height));
            }
            else
            {
                var actionsWidth = actions.DesiredSize.Width;
                var contentWidth = Math.Max(0, final.Width - actionsWidth - Spacing);
                content.Arrange(new Rect(0, (final.Height - content.DesiredSize.Height) / 2, contentWidth, content.DesiredSize.Height));
                actions.Arrange(new Rect(final.Width - actionsWidth, (final.Height - actions.DesiredSize.Height) / 2, actionsWidth, actions.DesiredSize.Height));
            }

            return final;
        }

        private Size MeasureSingle(Size available)
        {
            if (InternalChildren.Count == 0) return new Size();
            InternalChildren[0].Measure(available);
            return InternalChildren[0].DesiredSize;
        }
    }
}
