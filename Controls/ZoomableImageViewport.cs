using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TensileNeW.Controls;

public sealed class ZoomableImageViewport
{
    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 16;
    private const double ZoomStep = 1.15;
    private readonly FrameworkElement _viewport;
    private readonly Canvas _canvas;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translation = new();
    private double _fitScale = 1;
    private double _userZoom = 1;
    private bool _isDragging;
    private Point _dragStart;
    private Point _translationStart;

    public ZoomableImageViewport(FrameworkElement viewport, Canvas canvas)
    {
        _viewport = viewport;
        _canvas = canvas;
        canvas.RenderTransform = new TransformGroup { Children = { _scale, _translation } };
        viewport.PreviewMouseWheel += OnMouseWheel;
        viewport.PreviewMouseLeftButtonDown += OnMouseDown;
        viewport.PreviewMouseMove += OnMouseMove;
        viewport.PreviewMouseLeftButtonUp += OnMouseUp;
        viewport.LostMouseCapture += OnLostMouseCapture;
        viewport.SizeChanged += OnSizeChanged;
    }

    public bool IsAdjusted { get; private set; }

    public void Reset()
    {
        if (_canvas.Width <= 0 || _canvas.Height <= 0 || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
        _fitScale = Math.Min(_viewport.ActualWidth / _canvas.Width, _viewport.ActualHeight / _canvas.Height);
        if (!double.IsFinite(_fitScale) || _fitScale <= 0) _fitScale = 1;
        _userZoom = 1;
        _scale.ScaleX = _fitScale;
        _scale.ScaleY = _fitScale;
        _translation.X = (_viewport.ActualWidth - _canvas.Width * _fitScale) / 2;
        _translation.Y = (_viewport.ActualHeight - _canvas.Height * _fitScale) / 2;
        IsAdjusted = false;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_canvas.Width <= 0 || _canvas.Height <= 0) return;
        Point pointer = e.GetPosition(_viewport);
        double targetZoom = Math.Clamp(_userZoom * (e.Delta > 0 ? ZoomStep : 1 / ZoomStep), MinimumZoom, MaximumZoom);
        double factor = targetZoom / _userZoom;
        _userZoom = targetZoom;
        IsAdjusted = true;
        _translation.X = pointer.X - (pointer.X - _translation.X) * factor;
        _translation.Y = pointer.Y - (pointer.Y - _translation.Y) * factor;
        _scale.ScaleX = _fitScale * _userZoom;
        _scale.ScaleY = _fitScale * _userZoom;
        ConstrainTranslation();
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_canvas.Width <= 0 || _canvas.Height <= 0) return;
        _isDragging = true;
        _dragStart = e.GetPosition(_viewport);
        _translationStart = new Point(_translation.X, _translation.Y);
        IsAdjusted = true;
        _viewport.Cursor = Cursors.SizeAll;
        _viewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        Point pointer = e.GetPosition(_viewport);
        _translation.X = _translationStart.X + pointer.X - _dragStart.X;
        _translation.Y = _translationStart.Y + pointer.Y - _dragStart.Y;
        ConstrainTranslation();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) _viewport.ReleaseMouseCapture();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDragging = false;
        _viewport.Cursor = Cursors.Arrow;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsAdjusted) Reset();
        else ConstrainTranslation();
    }

    private void ConstrainTranslation()
    {
        if (_canvas.Width <= 0 || _canvas.Height <= 0 || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
        double scaledWidth = _canvas.Width * _scale.ScaleX;
        double scaledHeight = _canvas.Height * _scale.ScaleY;
        _translation.X = scaledWidth <= _viewport.ActualWidth
            ? (_viewport.ActualWidth - scaledWidth) / 2
            : Math.Clamp(_translation.X, _viewport.ActualWidth - scaledWidth, 0);
        _translation.Y = scaledHeight <= _viewport.ActualHeight
            ? (_viewport.ActualHeight - scaledHeight) / 2
            : Math.Clamp(_translation.Y, _viewport.ActualHeight - scaledHeight, 0);
    }
}