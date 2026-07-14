using ComicTrans.Models;
using ComicTrans.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ComicTrans;

public partial class MainWindow : Window
{
    private readonly OcrService _ocrService = new();

    private List<OcrResult> _ocrResults = new();

    private readonly List<Rectangle> _rectangles = new();

    private string? _currentImagePath;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void btnOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new()
        {
            Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dlg.ShowDialog() != true)
            return;

        _currentImagePath = dlg.FileName;

        BitmapImage bitmap = new();

        bitmap.BeginInit();
        bitmap.UriSource = new Uri(_currentImagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        imgComic.Source = bitmap;

        overlayCanvas.Width = bitmap.PixelWidth;
        overlayCanvas.Height = bitmap.PixelHeight;

        overlayCanvas.Children.Clear();
        lvOcrResult.ItemsSource = null;
        _rectangles.Clear();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath))
        {
            MessageBox.Show("Vui lòng mở ảnh trước.");
            return;
        }

        try
        {
            btnOCR.IsEnabled = false;

            _ocrResults = await _ocrService.RecognizeAsync(_currentImagePath);

            DrawOcrBoxes();

            lvOcrResult.ItemsSource = _ocrResults;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            btnOCR.IsEnabled = true;
        }
    }

    private void DrawOcrBoxes()
    {
        overlayCanvas.Children.Clear();
        _rectangles.Clear();

        foreach (var item in _ocrResults)
        {
            if (item.Box.Count != 4)
                continue;

            double left = item.Box.Min(p => p[0]);
            double top = item.Box.Min(p => p[1]);
            double right = item.Box.Max(p => p[0]);
            double bottom = item.Box.Max(p => p[1]);

            Rectangle rect = new()
            {
                Width = right - left,
                Height = bottom - top,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);

            overlayCanvas.Children.Add(rect);
            _rectangles.Add(rect);
        }
    }

    private void lvOcrResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = lvOcrResult.SelectedIndex;

        if (index < 0 || index >= _rectangles.Count)
            return;

        foreach (var rect in _rectangles)
        {
            rect.Stroke = Brushes.Red;
            rect.StrokeThickness = 2;
        }

        _rectangles[index].Stroke = Brushes.LimeGreen;
        _rectangles[index].StrokeThickness = 4;
    }
}