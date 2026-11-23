using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nyatch.ThemeEditor;

/// <summary>A picture that plays its frames (a GIF) for as long as it is on screen, as Nyatch plays them.</summary>
internal sealed class AnimatedImage : Image
{
    private readonly DispatcherTimer _timer = new();
    private Picture? _picture;
    private int _frame;

    public AnimatedImage()
    {
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _timer.Tick += (_, _) => Next();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Still: the first frame only (lists of many pictures).</summary>
    public bool Still { get; set; }

    public Picture? Picture
    {
        get => _picture;
        set
        {
            _picture = value;
            Restart();
        }
    }

    private void Restart()
    {
        _timer.Stop();
        _frame = 0;
        Source = _picture?.Frames[0];
        if (_picture is null || Still || _picture.Frames.Count < 2 || !IsLoaded) return;
        _timer.Interval = TimeSpan.FromSeconds(_picture.Delays[0]);
        _timer.Start();
    }

    private void Next()
    {
        if (_picture is null) return;
        _frame = (_frame + 1) % _picture.Frames.Count;
        Source = _picture.Frames[_frame];
        _timer.Interval = TimeSpan.FromSeconds(_picture.Delays[_frame]);
    }
}
