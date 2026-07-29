using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexUsageBar.Windows.Interop;
using CodexUsageBar.Windows.Tray;

namespace CodexUsageBar.Windows.Tests;

public sealed class SystemTrayProgressIconRendererTests
{
    [Fact]
    public void CreateIcon_ProducesReadableAlphaIconAndReleasesIt() => RunSta(() =>
    {
        var state = new SystemTrayIconState(
            72d,
            "72",
            Colors.White,
            Color.FromRgb(0x2F, 0x2F, 0x2F),
            Color.FromRgb(0x8D, 0x9E, 0xFC),
            Color.FromRgb(0x58, 0x5E, 0xF6),
            Color.FromRgb(0x4E, 0x4F, 0xF4));
        var iconHandle = SystemTrayProgressIconRenderer.CreateIcon(state);
        Assert.NotEqual(0, iconHandle);
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                null);
            Assert.Equal(32, bitmap.PixelWidth);
            Assert.Equal(32, bitmap.PixelHeight);

            var converted = new FormatConvertedBitmap(
                bitmap,
                PixelFormats.Bgra32,
                null,
                0d);
            var pixels = new byte[32 * 32 * 4];
            converted.CopyPixels(pixels, 32 * 4, 0);
            Assert.Contains(
                Enumerable.Range(0, 32 * 32),
                pixel => pixels[(pixel * 4) + 3] > 0);
            Assert.True(
                HasVisiblePixel(pixels, xStart: 14, yStart: 0, width: 4, height: 2),
                "The ring should fill the icon canvas up to its outer edge.");
            Assert.True(
                HasVisiblePixel(pixels, xStart: 9, yStart: 10, width: 14, height: 12),
                "The quota number should be visible in the center.");
        }
        finally
        {
            Assert.True(NativeMethods.DestroyIcon(iconHandle));
        }
    });

    private static bool HasVisiblePixel(
        byte[] pixels,
        int xStart,
        int yStart,
        int width,
        int height)
    {
        for (var y = yStart; y < yStart + height; y++)
        {
            for (var x = xStart; x < xStart + width; x++)
            {
                if (pixels[((y * 32 + x) * 4) + 3] > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Theory]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(96d)]
    public void RoundedCaps_VisibleSweepMatchesProgress(double progress)
    {
        const double stroke = 5d;
        const double radius = 10.5d;

        var centerlineSweep =
            SystemTrayProgressIconRenderer.CalculateCenterlineSweepDegrees(
                progress,
                stroke,
                radius);
        var roundedCapsSweep = stroke / radius * 180d / Math.PI;

        Assert.Equal(progress * 3.6d, centerlineSweep + roundedCapsSweep, precision: 8);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("STA test did not complete within 10 seconds.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
