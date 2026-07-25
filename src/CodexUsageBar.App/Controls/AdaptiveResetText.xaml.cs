using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexUsageBar.App.Controls;

public partial class AdaptiveResetText : UserControl
{
    internal const double MinimumHorizontalScale = 0.78;
    private const double SingleLineSplitSafetyDip = 12d;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(AdaptiveResetText),
        new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    private bool isInitialized;

    public AdaptiveResetText()
    {
        InitializeComponent();
        isInitialized = true;
        Loaded += (_, _) => UpdateAdaptiveLayout(ActualWidth);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var availableWidth = double.IsFinite(constraint.Width) ? constraint.Width : ActualWidth;
        UpdateAdaptiveLayout(availableWidth);
        var desired = base.MeasureOverride(constraint);
        return double.IsFinite(constraint.Width)
            ? new Size(Math.Min(desired.Width, constraint.Width), desired.Height)
            : desired;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        UpdateAdaptiveLayout(arrangeBounds.Width);
        return base.ArrangeOverride(arrangeBounds);
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var control = (AdaptiveResetText)dependencyObject;
        control.UpdateAdaptiveLayout(control.ActualWidth);
        control.InvalidateMeasure();
    }

    private void UpdateAdaptiveLayout(double availableWidth)
    {
        if (!isInitialized)
        {
            return;
        }

        var rawText = Text ?? string.Empty;
        TextElement.Text = rawText;
        TextElement.ClearValue(WidthProperty);
        TextElement.RenderTransform = Transform.Identity;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return;
        }

        var displayText = rawText;
        var naturalWidth = MeasureLine(rawText);
        var layoutWidth = ToLayoutWidth(naturalWidth);
        var horizontalScale = CalculateScale(availableWidth, layoutWidth);

        if (TrySplitAtLastSpace(rawText, out var firstLine, out var secondLine))
        {
            displayText = $"{firstLine}{Environment.NewLine}{secondLine}";
            naturalWidth = Math.Max(MeasureLine(firstLine), MeasureLine(secondLine));
            layoutWidth = ToLayoutWidth(naturalWidth);
            horizontalScale = CalculateScale(availableWidth, layoutWidth);
        }

        TextElement.Text = displayText;
        TextElement.Width = layoutWidth;
        TextElement.RenderTransform = new ScaleTransform(horizontalScale, 1d);
    }

    private double MeasureLine(string text)
    {
        var typeface = new Typeface(
            TextElement.FontFamily,
            TextElement.FontStyle,
            TextElement.FontWeight,
            TextElement.FontStretch);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            TextElement.FlowDirection,
            typeface,
            TextElement.FontSize,
            TextElement.Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static double CalculateScale(double availableWidth, double naturalWidth) =>
        naturalWidth <= 0 ? 1d : Math.Min(1d, availableWidth / naturalWidth);

    private static double ToLayoutWidth(double measuredWidth) =>
        measuredWidth <= 0 ? 0d : Math.Ceiling(measuredWidth) + 1d;

    private static bool TrySplitAtLastSpace(string text, out string firstLine, out string secondLine)
    {
        var splitIndex = text.LastIndexOf(' ');
        if (splitIndex <= 0 || splitIndex >= text.Length - 1)
        {
            firstLine = string.Empty;
            secondLine = string.Empty;
            return false;
        }

        firstLine = text[..splitIndex];
        secondLine = text[(splitIndex + 1)..];
        return true;
    }
}
