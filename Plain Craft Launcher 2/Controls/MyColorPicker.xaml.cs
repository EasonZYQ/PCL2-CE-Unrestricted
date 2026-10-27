using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

public partial class MyColorPicker
{
    private bool _updatingUi;
    private double _hue;
    private double _saturation;
    private double _value;

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(MyColorPicker),
        new PropertyMetadata(Color.FromRgb(0x52, 0x91, 0xDF), (d, _) => ((MyColorPicker)d).RefreshFromColor()));

    /// <summary>
    ///     当前选中的颜色。
    /// </summary>
    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>
    ///     用户确认颜色后触发，拖动过程中不会触发。
    /// </summary>
    public event EventHandler<Color>? ColorCommitted;

    /// <summary>
    ///     点击“恢复初始颜色”后触发。
    /// </summary>
    public event EventHandler? ResetClicked;

    public MyColorPicker()
    {
        InitializeComponent();
        PanSv.SizeChanged += (_, _) => UpdateThumbs();
        PanHue.SizeChanged += (_, _) => UpdateThumbs();
        Loaded += (_, _) => RefreshFromColor();
    }

    private void RefreshFromColor()
    {
        if (TextHex is null) return;
        var color = SelectedColor;
        ColorToHsv(color, out _hue, out _saturation, out _value);
        RefreshUi();
    }

    private void RefreshUi()
    {
        _updatingUi = true;
        var hueColor = ColorFromHsv(_hue, 1d, 1d);
        RectHue.Fill = new SolidColorBrush(hueColor);
        var color = ColorFromHsv(_hue, _saturation, _value);
        PanPreview.Background = new SolidColorBrush(color);
        TextHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        UpdateThumbs();
        _updatingUi = false;
    }

    private void UpdateThumbs()
    {
        if (ThumbSv is null || ThumbHue is null) return;
        ThumbSv.Margin = new Thickness(
            Math.Max(0d, (PanSv.ActualWidth - ThumbSv.Width) * _saturation),
            Math.Max(0d, (PanSv.ActualHeight - ThumbSv.Height) * (1d - _value)), 0, 0);
        ThumbHue.Margin = new Thickness(
            Math.Max(0d, (PanHue.ActualWidth - ThumbHue.Width) * (_hue / 360d)), 0, 0, 0);
    }

    private void PanSv_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PanSv.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(PanSv));
        e.Handled = true;
    }

    private void PanSv_MouseMove(object sender, MouseEventArgs e)
    {
        if (!PanSv.IsMouseCaptured) return;
        UpdateSvFromPoint(e.GetPosition(PanSv));
    }

    private void PanSv_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (PanSv.IsMouseCaptured) PanSv.ReleaseMouseCapture();
        CommitFromHsv();
        e.Handled = true;
    }

    private void UpdateSvFromPoint(Point position)
    {
        var width = Math.Max(1d, PanSv.ActualWidth - ThumbSv.Width);
        var height = Math.Max(1d, PanSv.ActualHeight - ThumbSv.Height);
        _saturation = Math.Clamp((position.X - ThumbSv.Width / 2d) / width, 0d, 1d);
        _value = 1d - Math.Clamp((position.Y - ThumbSv.Height / 2d) / height, 0d, 1d);
        RefreshUi();
    }

    private void PanHue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PanHue.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(PanHue));
        e.Handled = true;
    }

    private void PanHue_MouseMove(object sender, MouseEventArgs e)
    {
        if (!PanHue.IsMouseCaptured) return;
        UpdateHueFromPoint(e.GetPosition(PanHue));
    }

    private void PanHue_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (PanHue.IsMouseCaptured) PanHue.ReleaseMouseCapture();
        CommitFromHsv();
        e.Handled = true;
    }

    private void UpdateHueFromPoint(Point position)
    {
        var width = Math.Max(1d, PanHue.ActualWidth - ThumbHue.Width);
        _hue = Math.Clamp((position.X - ThumbHue.Width / 2d) / width, 0d, 1d) * 360d;
        RefreshUi();
    }

    private void TextHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingUi) return;
        if (!PCL.Core.UI.Theme.ThemeService.TryParseHexColor(TextHex.Text, out var color)) return;
        if (color == SelectedColor) return;
        SelectedColor = color;
        ColorCommitted?.Invoke(this, color);
    }

    private void CommitFromHsv()
    {
        var color = ColorFromHsv(_hue, _saturation, _value);
        if (color != SelectedColor) SelectedColor = color;
        ColorCommitted?.Invoke(this, SelectedColor);
    }

    private void BtnReset_Click(object sender, MouseButtonEventArgs e)
    {
        ResetClicked?.Invoke(this, EventArgs.Empty);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360d + 360d) % 360d;
        saturation = Math.Clamp(saturation, 0d, 1d);
        value = Math.Clamp(value, 0d, 1d);
        var chroma = value * saturation;
        var x = chroma * (1d - Math.Abs(hue / 60d % 2d - 1d));
        var m = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60d => (chroma, x, 0d),
            < 120d => (x, chroma, 0d),
            < 180d => (0d, chroma, x),
            < 240d => (0d, x, chroma),
            < 300d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        value = max;
        saturation = max <= 0d ? 0d : delta / max;
        if (delta <= 0d)
        {
            hue = 0d;
            return;
        }

        double rawHue;
        if (max == r) rawHue = 60d * ((g - b) / delta % 6d);
        else if (max == g) rawHue = 60d * ((b - r) / delta + 2d);
        else rawHue = 60d * ((r - g) / delta + 4d);
        hue = (rawHue + 360d) % 360d;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d);
}
