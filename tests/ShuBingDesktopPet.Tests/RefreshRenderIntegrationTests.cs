using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class RefreshRenderIntegrationTests
{
    [Theory]
    [InlineData(0.75)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void ObsRenderPath_RendersAllActionFamiliesAtStableScale(double scale)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = FindAssetRoot();
                var sprite = SpriteSheet.Load(root);
                var frameCache = new SpriteFrameCache(sprite.Source!, sprite.Rows, sprite.Columns,
                    sprite.FrameWidth, sprite.FrameHeight);
                var feeding = new FeedingFrameCache(root);
                var longBody = new WalkingBodyFrameCache(root);
                foreach (var bodyStyle in new[] { WalkingBodyStyle.Current, WalkingBodyStyle.LongBody })
                {
                    var settings = new PetSettings
                    {
                        Scale = scale,
                        RoamingEnabled = true,
                        WalkingBodyStyle = bodyStyle,
                        ObsCanvasWidth = 1920,
                        ObsCanvasHeight = 1080
                    };
                    using var probe = new RenderProbe(sprite, frameCache, feeding, longBody, settings);
                    for (var row = 0; row < sprite.Rows; row++)
                    for (var column = 0; column < AnimationPlanFactory.RowSpec(row).FrameCount; column++)
                    {
                        probe.RenderRow(row, column);
                        var expected = bodyStyle == WalkingBodyStyle.LongBody && (row is 1 or 2)
                            ? longBody.GetFrame(row == 1, column)
                            : frameCache.GetFrame(row, column);
                        probe.AssertRenderedFrame(expected, expected!.PixelWidth, bodyStyle, scale);
                    }
                    foreach (var food in new[] { PetFoodType.Pellet, PetFoodType.Broccoli, PetFoodType.BokChoy })
                    for (var column = 0; column < 6; column++)
                    {
                        probe.RenderFood(food, column);
                        probe.AssertRenderedFrame(feeding.GetFrame(food, column), 192, bodyStyle, scale);
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("WPF integration test thread must not hang");
        failure.Should().BeNull();
    }

    private sealed class RenderProbe : IDisposable
    {
        private readonly ObsCaptureWindow _window;
        private readonly FrameworkElement _viewbox;
        private readonly Image _image;
        private readonly ScaleTransform _scale;
        private readonly TranslateTransform _offset;
        private readonly object _sequencer;
        private readonly MethodInfo _renderFrame;

        public RenderProbe(SpriteSheet sprite, SpriteFrameCache cache, FeedingFrameCache feeding,
            WalkingBodyFrameCache longBody, PetSettings settings)
        {
            _window = new ObsCaptureWindow(sprite, cache, feeding, longBody, settings);
            StopTimer();
            _viewbox = GetField<FrameworkElement>("PetViewbox");
            _image = GetField<Image>("FrameImage");
            var transform = (TransformGroup)_image.RenderTransform;
            _scale = (ScaleTransform)transform.Children[0];
            _offset = (TranslateTransform)transform.Children[1];
            _sequencer = GetField<object>("_sequencer");
            _renderFrame = typeof(ObsCaptureWindow).GetMethod("RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        }

        public void RenderRow(int row, int column)
        {
            _window.ApplyPetState(null, false, PetFoodType.None);
            typeof(ObsCaptureWindow).GetField("_isRoaming", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_window, row is 1 or 2);
            typeof(ObsCaptureWindow).GetField("_roamDirection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_window, new Vector(1, 0));
            var plan = AnimationPlanFactory.CreateRowPlan(row, fixedColumn: column);
            _sequencer.GetType().GetMethod("ApplyPlan")!.Invoke(_sequencer,
                new object[] { plan, AnimationPlanFactory.RowSpec(row) });
            Render();
        }

        public void RenderFood(PetFoodType food, int column)
        {
            _window.ApplyPetState(PetAction.Feed, true, food, column);
            Render();
        }

        public void AssertRenderedFrame(BitmapSource? expected, int sourceWidth, WalkingBodyStyle bodyStyle, double scale)
        {
            expected.Should().NotBeNull();
            _image.Source.Should().NotBeNull();
            var source = (BitmapSource)_image.Source;
            ReferenceEquals(source, expected).Should().BeTrue("render must use the shared cache bitmap");
            source.PixelWidth.Should().Be(sourceWidth);
            source.PixelHeight.Should().Be(208);
            var outerWidth = bodyStyle == WalkingBodyStyle.LongBody ? 256 : 192;
            _viewbox.Width.Should().BeApproximately(outerWidth * scale, 0.01);
            _viewbox.Height.Should().BeApproximately(208 * scale, 0.01);

            _viewbox.Measure(new Size(_viewbox.Width, _viewbox.Height));
            _viewbox.Arrange(new Rect(0, 0, _viewbox.Width, _viewbox.Height));
            _viewbox.UpdateLayout();
            var rendered = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(_viewbox.Width)), Math.Max(1, (int)Math.Round(_viewbox.Height)),
                96, 96, PixelFormats.Pbgra32);
            rendered.Render(_viewbox);
            rendered.PixelWidth.Should().Be(Math.Max(1, (int)Math.Round(outerWidth * scale)));
            rendered.PixelHeight.Should().Be(Math.Max(1, (int)Math.Round(208 * scale)));
            var actualBox = AlphaBoundingBox(rendered);
            actualBox.Should().NotBeNull("the WPF render should contain visible sprite pixels");
            var sourceBox = AlphaBoundingBox(source);
            sourceBox.Should().NotBeNull();
            var expectedLeft = (outerWidth - sourceWidth) / 2.0 + sourceBox!.Value.Left;
            actualBox!.Value.Left.Should().BeInRange((int)Math.Floor(expectedLeft * scale) - 2, (int)Math.Ceiling(expectedLeft * scale) + 2);
            actualBox.Value.Right.Should().BeInRange((int)Math.Floor((expectedLeft + sourceBox.Value.Width) * scale) - 2, (int)Math.Ceiling((expectedLeft + sourceBox.Value.Width) * scale) + 2);
            actualBox.Value.Top.Should().BeInRange((int)Math.Floor(sourceBox.Value.Top * scale) - 2, (int)Math.Ceiling(sourceBox.Value.Top * scale) + 2);
            actualBox.Value.Bottom.Should().BeInRange((int)Math.Floor(sourceBox.Value.Bottom * scale) - 2, (int)Math.Ceiling(sourceBox.Value.Bottom * scale) + 2);
            AssertNeutralTransforms();
        }

        public void AssertNeutralTransforms()
        {
            _scale.ScaleX.Should().Be(1);
            _scale.ScaleY.Should().Be(1);
            _offset.X.Should().Be(0);
            _offset.Y.Should().Be(0);
        }

        private void Render() => _renderFrame.Invoke(_window, null);

        private static Rect? AlphaBoundingBox(BitmapSource source)
        {
            var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            var minX = source.PixelWidth; var minY = source.PixelHeight; var maxX = -1; var maxY = -1;
            for (var y = 0; y < source.PixelHeight; y++)
            for (var x = 0; x < source.PixelWidth; x++)
                if (pixels[(y * source.PixelWidth + x) * 4 + 3] > 127)
                { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            return maxX < minX ? null : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private void StopTimer() =>
            ((System.Windows.Threading.DispatcherTimer)typeof(ObsCaptureWindow)
                .GetField("_tickTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_window)!).Stop();

        private T GetField<T>(string name) => (T)typeof(ObsCaptureWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!;

        public void Dispose()
        {
            StopTimer();
            _window.Close();
        }
    }

    private static string FindAssetRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var root = Path.Combine(directory.FullName, "src", "ShuBingDesktopPet");
            if (File.Exists(Path.Combine(root, "Assets", "spritesheet.png"))) return root;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate asset root.");
    }
}
