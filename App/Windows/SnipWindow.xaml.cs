using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrammarBubble.App.Interop;
using GrammarBubble.App.Services;

namespace GrammarBubble.App.Windows;

/// Overlay de recorte: congela la pantalla completa y deja arrastrar un área. Devuelve el PNG recortado.
public partial class SnipWindow : Window
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private readonly TaskCompletionSource<byte[]?> _result = new();
    private readonly System.Drawing.Bitmap _shot;
    private readonly double _scale;
    private System.Windows.Point? _start;

    public SnipWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _shot = CaptureScreen();
        // Píxeles de captura por unidad de WPF: 1 si el proceso no es consciente del DPI, 1.25 con escalado 125 %, etc.
        _scale = _shot.Width / Width;

        Shot.Source = ToBitmapSource(_shot);
        Shot.Width = Width;
        Shot.Height = Height;
        UpdateMask(Rect.Empty);

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
        Closed += (_, _) =>
        {
            _result.TrySetResult(null);
            _shot.Dispose();
        };
    }

    /// Muestra el overlay y espera a que el usuario seleccione un área (o cancele con Esc / clic derecho).
    public Task<byte[]?> ShowAsync()
    {
        Show();
        Activate();
        Focus();
        return _result.Task;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        Root.CaptureMouse();
        UpdateMask(Rect.Empty);
    }

    private void OnMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_start is null) return;
        ShowSelection(new Rect(_start.Value, e.GetPosition(Root)));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        Root.ReleaseMouseCapture();
        if (_start is null) return;

        var selection = new Rect(_start.Value, e.GetPosition(Root));
        _start = null;
        if (selection.Width < 4 || selection.Height < 4)
        {
            Cancel();
            return;
        }

        var crop = ToPixels(selection);
        using var cropped = _shot.Clone(crop, _shot.PixelFormat);
        _result.TrySetResult(OcrService.Prepare(cropped));
        Close();
    }

    private void OnCancel(object sender, MouseButtonEventArgs e) => Cancel();

    private void Cancel()
    {
        _result.TrySetResult(null);
        Close();
    }

    private void ShowSelection(Rect selection)
    {
        Canvas.SetLeft(Frame, selection.X);
        Canvas.SetTop(Frame, selection.Y);
        Frame.Width = selection.Width;
        Frame.Height = selection.Height;
        Frame.Visibility = Visibility.Visible;

        SizeLabel.Text = $"{(int)selection.Width} × {(int)selection.Height}";
        Canvas.SetLeft(SizeLabel, selection.X);
        Canvas.SetTop(SizeLabel, selection.Y > 24 ? selection.Y - 22 : selection.Y + 4);
        SizeLabel.Visibility = Visibility.Visible;

        UpdateMask(selection);
    }

    /// Oscurece toda la pantalla salvo el área seleccionada.
    private void UpdateMask(Rect selection)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Width, Height));
        Dim.Data = selection.IsEmpty || selection.Width == 0
            ? full
            : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(selection));

        if (selection.IsEmpty || selection.Width == 0)
        {
            Frame.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
        }
    }

    private System.Drawing.Rectangle ToPixels(Rect selection)
    {
        var x = Math.Max(0, (int)(selection.X * _scale));
        var y = Math.Max(0, (int)(selection.Y * _scale));
        var right = Math.Min(_shot.Width, (int)(selection.Right * _scale));
        var bottom = Math.Min(_shot.Height, (int)(selection.Bottom * _scale));
        return new System.Drawing.Rectangle(x, y, right - x, bottom - y);
    }

    private static System.Drawing.Bitmap CaptureScreen()
    {
        var x = Native.GetSystemMetrics(SmXVirtualScreen);
        var y = Native.GetSystemMetrics(SmYVirtualScreen);
        var width = Native.GetSystemMetrics(SmCxVirtualScreen);
        var height = Native.GetSystemMetrics(SmCyVirtualScreen);

        var bitmap = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        return bitmap;
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
