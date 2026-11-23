using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nyatch.Core;

namespace Nyatch.ThemeEditor;

/// <summary>
/// Cropping a slot's picture: a box over the uncut picture, kept to the slot's shape (or square, or
/// free), moved by dragging inside it and sized by its corners. The place above shows the result as it
/// goes. Apply cuts the file; the uncut picture is kept, so Uncrop puts it back.
/// </summary>
internal sealed partial class MainWindow
{
    private sealed class CropState
    {
        public required string Slot { get; init; }
        public required Picture Source { get; init; }  // the uncut picture
        public Rect Part { get; set; }                 // in the source's trimmed pixels
        public int Shape { get; set; }                 // 0 the slot's shape, 1 square, 2 free
    }

    private static readonly string[] CropShapes = ["Slot shape", "Square", "Free"];

    private CropState? _crop;
    private Border? _sceneCard;

    private static double? AspectOf(CharacterSlot slot, int shape) => shape switch
    {
        0 => (double)slot.Width / slot.Height,
        1 => 1,
        _ => null,
    };

    private void StartCrop(PackProject pack, CharacterSlot slot)
    {
        if (Load(pack.OriginalOf(slot.Name) ?? pack.PictureOf(slot.Name)) is not { } source) return;
        _crop = new CropState { Slot = slot.Name, Source = source };
        _crop.Part = Fitted(source, AspectOf(slot, 0));
        DrawPreview();
    }

    /// <summary>For pictures of the window: open the crop on the chosen slot.</summary>
    public void CropChosen()
    {
        if (_pack is { } pack && _slot is { } slot) StartCrop(pack, slot);
    }

    /// <summary>The largest box of a shape that fits the picture, in its middle (the whole picture when free).</summary>
    private static Rect Fitted(Picture source, double? aspect)
    {
        double width = source.TrimmedWidth, height = source.TrimmedHeight;
        if (aspect is not { } a) return new Rect(0, 0, width, height);
        double w = width, h = width / a;
        if (h > height)
        {
            h = height;
            w = height * a;
        }
        return new Rect((width - w) / 2, (height - h) / 2, w, h);
    }

    private static Int32Rect Pixels(Rect part, Picture source)
    {
        var x = Math.Clamp((int)Math.Round(part.X), 0, source.TrimmedWidth - 1);
        var y = Math.Clamp((int)Math.Round(part.Y), 0, source.TrimmedHeight - 1);
        var w = Math.Clamp((int)Math.Round(part.Width), 1, source.TrimmedWidth - x);
        var h = Math.Clamp((int)Math.Round(part.Height), 1, source.TrimmedHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    /// <summary>While cropping: the picture as the crop would make it.</summary>
    private Picture? CropShown(CharacterSlot slot) =>
        _crop is { } crop && crop.Slot == slot.Name ? crop.Source.Cropped(Pixels(crop.Part, crop.Source)) : null;

    private void RefreshScene()
    {
        if (_sceneCard is not null && _slot is { } slot && CropShown(slot) is { } shown) _sceneCard.Child = Scene(slot, shown);
    }

    private FrameworkElement CropEditor(PackProject pack, CharacterSlot slot)
    {
        var crop = _crop!;
        var source = crop.Source;
        double width = source.TrimmedWidth, height = source.TrimmedHeight;
        var scale = Math.Min(4, Math.Min(400 / width, 300 / height));
        double shownW = width * scale, shownH = height * scale;

        var panel = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var shape = new ComboBox { Width = 140, ItemsSource = CropShapes, SelectedIndex = crop.Shape };
        DockPanel.SetDock(shape, Dock.Right);
        top.Children.Add(shape);
        top.Children.Add(new TextBlock { Text = "Crop", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(top);

        // ----- The picture with the box over it -----
        var stage = new Grid { Width = shownW, Height = shownH, HorizontalAlignment = HorizontalAlignment.Left, Background = Checker() };
        stage.Children.Add(new AnimatedImage { Picture = source, Width = shownW, Height = shownH, Stretch = Stretch.Fill });
        var overlay = new Canvas { Width = shownW, Height = shownH, Background = Brushes.Transparent, ClipToBounds = true };
        var mask = new Path { Fill = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), IsHitTestVisible = false };
        var box = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
        var handles = new Rectangle[4];
        overlay.Children.Add(mask);
        overlay.Children.Add(box);
        for (var i = 0; i < 4; i++)
        {
            handles[i] = new Rectangle { Width = 12, Height = 12, RadiusX = 3, RadiusY = 3, Fill = Brushes.White, Stroke = new SolidColorBrush(Accent()), StrokeThickness = 1.5, IsHitTestVisible = false };
            overlay.Children.Add(handles[i]);
        }
        stage.Children.Add(overlay);
        panel.Children.Add(new Border { Child = stage, CornerRadius = new CornerRadius(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left });
        var size = Quiet("", 12);
        size.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(size);

        Point[] Corners(Rect r) => [r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight];
        void Place()
        {
            var r = crop.Part;
            var shown = new Rect(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);
            mask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, shownW, shownH)), new RectangleGeometry(shown));
            Canvas.SetLeft(box, shown.X);
            Canvas.SetTop(box, shown.Y);
            box.Width = shown.Width;
            box.Height = shown.Height;
            var corners = Corners(shown);
            for (var i = 0; i < 4; i++)
            {
                Canvas.SetLeft(handles[i], corners[i].X - 6);
                Canvas.SetTop(handles[i], corners[i].Y - 6);
            }
            var kept = Pixels(r, source);
            size.Text = $"Keeps {kept.Width} × {kept.Height} px of {source.TrimmedWidth} × {source.TrimmedHeight}. Drag to move, drag a corner to resize.";
        }
        Place();

        // ----- Dragging -----
        var mode = -1;  // 0-3 a corner, 4 moving
        var anchor = new Point();
        var offset = new Vector();
        Point At(MouseEventArgs e)
        {
            var p = e.GetPosition(overlay);
            return new Point(Math.Clamp(p.X / scale, 0, width), Math.Clamp(p.Y / scale, 0, height));
        }
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            var p = At(e);
            var r = crop.Part;
            var corners = Corners(r);
            mode = -1;
            for (var i = 0; i < 4; i++)
            {
                if ((corners[i] - p).Length * scale > 14) continue;
                mode = i;
                anchor = corners[3 - i];
                break;
            }
            if (mode < 0 && r.Contains(p))
            {
                mode = 4;
                offset = p - r.TopLeft;
            }
            if (mode < 0) return;
            overlay.CaptureMouse();
            e.Handled = true;
        };
        overlay.MouseMove += (_, e) =>
        {
            var p = At(e);
            if (mode < 0)
            {
                var r0 = crop.Part;
                var near = Array.Exists(Corners(r0), c => (c - p).Length * scale <= 14);
                overlay.Cursor = near ? Cursors.SizeNWSE : r0.Contains(p) ? Cursors.SizeAll : null;
                return;
            }
            var r = crop.Part;
            if (mode == 4)
            {
                crop.Part = new Rect(Math.Clamp(p.X - offset.X, 0, width - r.Width), Math.Clamp(p.Y - offset.Y, 0, height - r.Height), r.Width, r.Height);
            }
            else crop.Part = Resized(anchor, p, AspectOf(slot, crop.Shape), width, height);
            Place();
        };
        overlay.MouseLeftButtonUp += (_, _) =>
        {
            if (mode < 0) return;
            mode = -1;
            overlay.ReleaseMouseCapture();
            RefreshScene();
        };
        shape.SelectionChanged += (_, _) =>
        {
            crop.Shape = shape.SelectedIndex;
            crop.Part = Fitted(source, AspectOf(slot, crop.Shape));
            Place();
            RefreshScene();
        };

        // ----- Apply -----
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var apply = new Button { Content = "Apply crop", Style = (Style)FindResource("AccentButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        apply.Click += (_, _) =>
        {
            var kept = Pixels(crop.Part, source);
            try
            {
                pack.Crop(slot.Name, new Int32Rect(kept.X + source.TrimLeft, kept.Y + source.TrimTop, kept.Width, kept.Height));
                Say($"{slot.Name} cropped. Restore original undoes it.");
            }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
            {
                Say($"Failed: {error.Message}");
            }
            _crop = null;
            Changed();
        };
        buttons.Children.Add(apply);
        var reset = new Button { Content = "Reset", Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) =>
        {
            crop.Part = Fitted(source, AspectOf(slot, crop.Shape));
            Place();
            RefreshScene();
        };
        buttons.Children.Add(reset);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) =>
        {
            _crop = null;
            DrawPreview();
        };
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        return panel;
    }

    /// <summary>A box from a fixed corner to the pointer, kept to a shape and inside the picture.</summary>
    private static Rect Resized(Point anchor, Point pointer, double? aspect, double width, double height)
    {
        double dx = pointer.X - anchor.X, dy = pointer.Y - anchor.Y;
        var right = dx >= 0;
        var down = dy >= 0;
        double w = Math.Abs(dx), h = Math.Abs(dy);
        var maxW = right ? width - anchor.X : anchor.X;
        var maxH = down ? height - anchor.Y : anchor.Y;
        if (aspect is { } a)
        {
            if (w / Math.Max(h, 0.001) > a) h = w / a;
            else w = h * a;
            if (w > maxW)
            {
                w = maxW;
                h = w / a;
            }
            if (h > maxH)
            {
                h = maxH;
                w = h * a;
            }
        }
        else
        {
            w = Math.Min(w, maxW);
            h = Math.Min(h, maxH);
        }
        w = Math.Max(w, 4);
        h = Math.Max(h, 4);
        return new Rect(right ? anchor.X : anchor.X - w, down ? anchor.Y : anchor.Y - h, w, h);
    }

    private static Brush Checker()
    {
        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4C)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x64)), null, Geometry.Parse("M0,0 H8 V8 H0Z M8,8 H16 V16 H8Z")),
                },
            },
        };
        brush.Freeze();
        return brush;
    }
}
