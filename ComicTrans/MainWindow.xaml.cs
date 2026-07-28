using ComicTrans.Models;
using ComicTrans.Services;
using ComicTrans.ViewModels;
using ComicTrans.Helpers;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ComicTrans;

public partial class MainWindow : Window
{
    private readonly OcrService _ocrService = new();
    private readonly TranslationService _translationService = new();
    private readonly MainViewModel _viewModel;

    private readonly List<Rectangle> _rectangles = new();
    private string? _currentImagePath;

    private Point _dragStartPoint;
    private PageItem? _draggedItem;
    private System.Diagnostics.Process? _ocrProcess;

    // Các biến trạng thái hỗ trợ quét OCR thủ công
    private bool _isDrawingSelection;
    private Point _manualOcrStartPoint;
    private Rectangle? _selectionRect;

    // Các biến hỗ trợ kéo thả sắp xếp kết quả OCR
    private Point _ocrDragStartPoint;
    private OcrResult? _ocrDraggedItem;

    // Các biến trạng thái quản lý Zoom & Pan
    private double _zoomScale = 1.0;
    private const double MinScale = 0.1;
    private const double MaxScale = 5.0;
    private bool _isPanning;
    private Point _panStartMousePosition;
    private Point _panStartScrollOffset;
    private bool _isFirstLayout = true;

    public MainWindow()
    {
        InitializeComponent();
        
        // Khởi tạo ViewModel và cấu hình các Delegate kết nối UI
        _viewModel = new MainViewModel();

        _viewModel.RequestFilesSelection = (filter) =>
        {
            OpenFileDialog dlg = new()
            {
                Filter = filter,
                Multiselect = true
            };
            return dlg.ShowDialog() == true ? dlg.FileNames.ToList() : null;
        };

        _viewModel.RequestFolderSelection = (title) =>
        {
            var folderDlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title
            };
            return folderDlg.ShowDialog() == true ? folderDlg.FolderName : null;
        };

        _viewModel.ShowConfirmDialog = (message, title, tag) =>
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        };

        _viewModel.ShowMessage = (message, title, isError) =>
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);
        };

        _viewModel.SaveImageDelegate = async (page, path) =>
        {
            await Task.Run(() => 
            {
                Dispatcher.Invoke(() => 
                {
                    SaveReplacedImage(page, path);
                });
            });
        };

        // Lắng nghe sự kiện thay đổi trạng thái chọn trang để nạp ảnh/vẽ lại canvas
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        this.DataContext = _viewModel;
        StartOcrService();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage))
        {
            if (_viewModel.SelectedPage != null)
            {
                LoadPage(_viewModel.SelectedPage);
            }
            else
            {
                ClearPageDisplay();
            }
        }
    }

    private void ClearPageDisplay()
    {
        imgComic.Source = null;
        overlayCanvas.Children.Clear();
        _rectangles.Clear();
        _currentImagePath = null;
    }

    private void DrawOcrBoxes(PageItem page)
    {
        overlayCanvas.Children.Clear();
        _rectangles.Clear();

        if (page.OcrResults == null) return;

        foreach (var item in page.OcrResults)
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
                Fill = Brushes.Transparent,
                Tag = item
            };

            ContextMenu contextMenu = new();
            MenuItem deleteItem = new() { Header = "Xóa OCR này" };
            deleteItem.Click += (s, e) =>
            {
                if (_viewModel.DeleteOcrCommand.CanExecute(item))
                {
                    _viewModel.DeleteOcrCommand.Execute(item);
                }
            };
            contextMenu.Items.Add(deleteItem);

            contextMenu.Items.Add(new Separator());

            MenuItem colorSubMenu = new() { Header = "Màu chữ dịch" };
            MenuItem blackColorItem = new() { Header = "Chữ màu đen", IsCheckable = true, IsChecked = item.TextColor != "White" };
            MenuItem whiteColorItem = new() { Header = "Chữ màu trắng", IsCheckable = true, IsChecked = item.TextColor == "White" };

            blackColorItem.Click += (s, e) =>
            {
                item.TextColor = "Black";
                blackColorItem.IsChecked = true;
                whiteColorItem.IsChecked = false;
            };

            whiteColorItem.Click += (s, e) =>
            {
                item.TextColor = "White";
                blackColorItem.IsChecked = false;
                whiteColorItem.IsChecked = true;
            };

            colorSubMenu.Items.Add(blackColorItem);
            colorSubMenu.Items.Add(whiteColorItem);
            contextMenu.Items.Add(colorSubMenu);

            rect.ContextMenu = contextMenu;

            rect.MouseLeftButtonDown += (s, e) =>
            {
                if (s is Rectangle r && r.Tag is OcrResult ocrResult)
                {
                    _viewModel.SelectedOcrResult = ocrResult;
                    e.Handled = true;
                }
            };

            rect.MouseRightButtonDown += (s, e) =>
            {
                if (s is Rectangle r && r.Tag is OcrResult ocrResult)
                {
                    _viewModel.SelectedOcrResult = ocrResult;
                    e.Handled = true;
                }
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);

            overlayCanvas.Children.Add(rect);
            _rectangles.Add(rect);
        }
    }

    private void lvOcrResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lvOcrResult.SelectedItem is OcrResult selectedItem)
        {
            foreach (var rect in _rectangles)
            {
                if (rect.Tag == selectedItem)
                {
                    rect.Stroke = Brushes.LimeGreen;
                    rect.StrokeThickness = 4;
                }
                else
                {
                    rect.Stroke = Brushes.Red;
                    rect.StrokeThickness = 2;
                }
            }
        }
    }

    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is OcrResult item)
        {
            lvOcrResult.SelectedItem = item;
        }
    }
    private void LoadPage(PageItem page)
    {
        _currentImagePath = (page.IsReplaced && !string.IsNullOrEmpty(page.CleanImagePath) && File.Exists(page.CleanImagePath))
            ? page.CleanImagePath
            : page.ImagePath;

        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(_currentImagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        imgComic.Source = bitmap;

        overlayCanvas.Width = bitmap.PixelWidth;
        overlayCanvas.Height = bitmap.PixelHeight;

        overlayCanvas.Children.Clear();
        _rectangles.Clear();

        if (page.OcrResults != null && page.OcrResults.Count > 0)
        {
            if (page.IsReplaced)
            {
                DrawTranslatedText(page);
            }
            else
            {
                DrawOcrBoxes(page);
            }
        }

        FitToScreen();
    }

    private void DrawTranslatedText(PageItem page)
    {
        overlayCanvas.Children.Clear();
        _rectangles.Clear();

        if (page.OcrResults == null) return;

        foreach (var item in page.OcrResults)
        {
            if (item.Box.Count != 4)
                continue;

            double left = item.Box.Min(p => p[0]);
            double top = item.Box.Min(p => p[1]);
            double right = item.Box.Max(p => p[0]);
            double bottom = item.Box.Max(p => p[1]);

            double width = right - left;
            double height = bottom - top;

            TextBox textBox = new()
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                Foreground = item.TextColor == "White" ? Brushes.White : Brushes.Black,
                AcceptsReturn = true,
                Padding = new Thickness(2),
                Margin = new Thickness(0),
                Tag = item
            };

            textBox.GotFocus += (s, e) =>
            {
                if (s is TextBox tb && tb.Tag is OcrResult ocrResult)
                {
                    _viewModel.SelectedOcrResult = ocrResult;
                }
            };

            ContextMenu contextMenu = new();
            MenuItem copyItem = new() { Command = ApplicationCommands.Copy, Header = "Sao chép" };
            MenuItem cutItem = new() { Command = ApplicationCommands.Cut, Header = "Cắt" };
            MenuItem pasteItem = new() { Command = ApplicationCommands.Paste, Header = "Dán" };
            MenuItem deleteItem = new() { Header = "Xóa OCR này" };
            deleteItem.Click += (s, e) =>
            {
                if (_viewModel.DeleteOcrCommand.CanExecute(item))
                {
                    _viewModel.DeleteOcrCommand.Execute(item);
                }
            };

            contextMenu.Items.Add(copyItem);
            contextMenu.Items.Add(cutItem);
            contextMenu.Items.Add(pasteItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteItem);

            contextMenu.Items.Add(new Separator());

            MenuItem colorSubMenu = new() { Header = "Màu chữ dịch" };
            MenuItem blackColorItem = new() { Header = "Chữ màu đen", IsCheckable = true, IsChecked = item.TextColor != "White" };
            MenuItem whiteColorItem = new() { Header = "Chữ màu trắng", IsCheckable = true, IsChecked = item.TextColor == "White" };

            blackColorItem.Click += (s, e) =>
            {
                item.TextColor = "Black";
                textBox.Foreground = Brushes.Black;
                if (textBox.Effect is System.Windows.Media.Effects.DropShadowEffect outline)
                {
                    outline.Color = Colors.White;
                }
                blackColorItem.IsChecked = true;
                whiteColorItem.IsChecked = false;
            };

            whiteColorItem.Click += (s, e) =>
            {
                item.TextColor = "White";
                textBox.Foreground = Brushes.White;
                if (textBox.Effect is System.Windows.Media.Effects.DropShadowEffect outline)
                {
                    outline.Color = Colors.Black;
                }
                blackColorItem.IsChecked = false;
                whiteColorItem.IsChecked = true;
            };

            colorSubMenu.Items.Add(blackColorItem);
            colorSubMenu.Items.Add(whiteColorItem);
            contextMenu.Items.Add(colorSubMenu);

            textBox.ContextMenu = contextMenu;

            // Tính toán cỡ chữ tối ưu lúc ban đầu hoặc lấy cỡ chữ tùy biến đã lưu
            if (item.FontSize > 0)
            {
                textBox.FontSize = item.FontSize;
            }
            else
            {
                textBox.FontSize = CalculateOptimalFontSize(item.Text, width, height);
            }

            // Ràng buộc 2 chiều văn bản dịch
            System.Windows.Data.Binding binding = new("Text")
            {
                Source = item,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            };
            textBox.SetBinding(TextBox.TextProperty, binding);

            // Tự động tìm lại cỡ chữ khi người dùng chỉnh sửa văn bản trực tiếp trên Canvas (chỉ khi chưa chỉnh cỡ chữ thủ công)
            textBox.TextChanged += (s, e) =>
            {
                if (s is TextBox tb && item.FontSize <= 0)
                {
                    tb.FontSize = CalculateOptimalFontSize(tb.Text, width, height);
                }
            };

            // Cho phép điều chỉnh cỡ chữ bằng cuộn chuột
            textBox.PreviewMouseWheel += (s, e) =>
            {
                if (s is TextBox tb)
                {
                    double step = e.Delta > 0 ? 0.5 : -0.5;
                    double currentSize = tb.FontSize;
                    double newSize = Math.Clamp(currentSize + step, 4, 100);
                    tb.FontSize = newSize;
                    item.FontSize = newSize; // Lưu lại kích cỡ chữ tùy biến
                    e.Handled = true; // Ngăn chặn cuộn lan ra ngoài ảnh
                }
            };

            // Viền chữ bằng hiệu ứng bóng mờ (Trắng nếu chữ đen, Đen nếu chữ trắng) để tăng độ tương phản trên nền tối
            var outlineEffect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = item.TextColor == "White" ? Colors.Black : Colors.White,
                BlurRadius = 3,
                ShadowDepth = 0,
                Opacity = 1.0
            };
            textBox.Effect = outlineEffect;

            Canvas.SetLeft(textBox, left);
            Canvas.SetTop(textBox, top);

            overlayCanvas.Children.Add(textBox);
        }
    }

    private double CalculateOptimalFontSize(string text, double width, double height)
    {
        if (string.IsNullOrEmpty(text)) return 12;

        double min = 6;
        double max = 40;
        double optimal = 12;

        for (int i = 0; i < 5; i++)
        {
            double mid = (min + max) / 2;
            FormattedText formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                mid,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = width,
                MaxTextHeight = double.PositiveInfinity
            };

            if (formattedText.Height <= height)
            {
                optimal = mid;
                min = mid + 0.5;
            }
            else
            {
                max = mid - 0.5;
            }
        }
        return optimal;
    }


    private void lbPages_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedItem = GetObjectAtPoint<PageItem>(lbPages, e.GetPosition(lbPages));
    }

    private void lbPages_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
        {
            Point mousePos = e.GetPosition(null);
            Vector diff = _dragStartPoint - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(lbPages, _draggedItem, DragDropEffects.Move);
            }
        }
    }

    private void lbPages_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(PageItem)))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void lbPages_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(PageItem)))
        {
            PageItem? droppedItem = e.Data.GetData(typeof(PageItem)) as PageItem;
            PageItem? targetItem = GetObjectAtPoint<PageItem>(lbPages, e.GetPosition(lbPages));

            if (droppedItem != null && targetItem != null && droppedItem != targetItem)
            {
                int oldIndex = _viewModel.Pages.IndexOf(droppedItem);
                int newIndex = _viewModel.Pages.IndexOf(targetItem);

                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _viewModel.Pages.Move(oldIndex, newIndex);
                    _viewModel.UpdatePageNumbers();
                }
            }
        }
    }

    private T? GetObjectAtPoint<T>(ListBox listBox, Point point) where T : class
    {
        HitTestResult hitTestResult = VisualTreeHelper.HitTest(listBox, point);
        DependencyObject? obj = hitTestResult?.VisualHit;
        while (obj != null && obj != listBox)
        {
            if (obj is ListBoxItem listBoxItem)
            {
                return listBoxItem.DataContext as T;
            }
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private string? FindOcrServicePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // 1. Kiểm tra thư mục con ngay tại thư mục chạy file exe (khi đã đóng gói)
        string localPath = System.IO.Path.Combine(baseDir, "OCRService");
        if (Directory.Exists(localPath))
        {
            return System.IO.Path.GetFullPath(localPath);
        }
        
        // 2. Kiểm tra thư mục tương đối lúc phát triển (lên 3 cấp thư mục)
        string devPath = System.IO.Path.Combine(baseDir, @"..\..\..\OCRService");
        if (Directory.Exists(devPath))
        {
            return System.IO.Path.GetFullPath(devPath);
        }
        
        return null;
    }

    private void StartOcrService()
    {
        string? ocrDir = FindOcrServicePath();
        if (ocrDir == null)
        {
            MessageBox.Show("Không tìm thấy thư mục dịch vụ OCRService để tự động chạy Flask server.");
            return;
        }

        string pythonExe = System.IO.Path.Combine(ocrDir, @"venv\Scripts\python.exe");
        string appPy = System.IO.Path.Combine(ocrDir, "app.py");

        if (!File.Exists(pythonExe) || !File.Exists(appPy))
        {
            MessageBox.Show($"Không tìm thấy python.exe hoặc app.py tại: {ocrDir}.\nVui lòng cài đặt venv trước.");
            return;
        }

        // Tự động dọn dẹp các tiến trình Python cũ đã khởi động từ venv này để tránh xung đột cổng 5000
        KillExistingOcrProcesses(pythonExe);

        try
        {
            txtStatus.Text = "Đang khởi động dịch vụ OCR (Flask)...";

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{appPy}\"",
                WorkingDirectory = ocrDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _ocrProcess = new System.Diagnostics.Process { StartInfo = startInfo };
            _ocrProcess.Start();

            txtStatus.Text = "Dịch vụ OCR đã được khởi động tự động.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi tự động khởi động dịch vụ OCR: {ex.Message}");
        }
    }

    private void KillExistingOcrProcesses(string pythonExePath)
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("python");
            foreach (var p in processes)
            {
                try
                {
                    string? exePath = p.MainModule?.FileName;
                    if (exePath != null && string.Equals(exePath, pythonExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"Killing existing OCR python process with PID {p.Id}");
                        p.Kill(true);
                    }
                }
                catch
                {
                    // Bỏ qua nếu không có quyền truy cập vào MainModule của tiến trình khác (ví dụ của OS hoặc user khác)
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cleaning up old python processes: {ex.Message}");
        }
    }


    private void SaveReplacedImage(PageItem page, string outputPath)
    {
        if (string.IsNullOrEmpty(page.CleanImagePath) || !File.Exists(page.CleanImagePath))
            return;

        // 1. Nạp ảnh sạch để lấy độ phân giải gốc
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(page.CleanImagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        double width = bitmap.PixelWidth;
        double height = bitmap.PixelHeight;

        // 2. Dựng cấu trúc Grid để render trong bộ nhớ
        Grid grid = new()
        {
            Width = width,
            Height = height
        };

        Image img = new()
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill
        };
        grid.Children.Add(img);

        Canvas canvas = new()
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent
        };
        grid.Children.Add(canvas);

        // 3. Vẽ đè tất cả các TextBox dịch lên Canvas đúng tọa độ
        foreach (var item in page.OcrResults)
        {
            if (item.Box.Count != 4)
                continue;

            double left = item.Box.Min(p => p[0]);
            double top = item.Box.Min(p => p[1]);
            double right = item.Box.Max(p => p[0]);
            double bottom = item.Box.Max(p => p[1]);

            double w = right - left;
            double h = bottom - top;

            TextBox textBox = new()
            {
                Width = w,
                Height = h,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                Foreground = item.TextColor == "White" ? Brushes.White : Brushes.Black,
                Text = item.Text,
                Padding = new Thickness(2),
                Margin = new Thickness(0)
            };

            // Sử dụng font size tùy biến nếu có, ngược lại tự động tính cỡ chữ tối ưu
            textBox.FontSize = item.FontSize > 0 ? item.FontSize : CalculateOptimalFontSize(item.Text, w, h);

            // Hiệu ứng bóng mờ viền chữ (Trắng nếu chữ đen, Đen nếu chữ trắng)
            var outlineEffect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = item.TextColor == "White" ? Colors.Black : Colors.White,
                BlurRadius = 3,
                ShadowDepth = 0,
                Opacity = 1.0
            };
            textBox.Effect = outlineEffect;

            Canvas.SetLeft(textBox, left);
            Canvas.SetTop(textBox, top);
            canvas.Children.Add(textBox);
        }

        // 4. Bắt buộc WPF đo đạc và sắp xếp bố cục trước khi chụp ảnh
        Size sz = new(width, height);
        grid.Measure(sz);
        grid.Arrange(new Rect(sz));
        grid.UpdateLayout();

        // 5. Kết xuất bằng RenderTargetBitmap
        RenderTargetBitmap rtb = new((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);

        // 6. Mã hóa và ghi file xuống đĩa
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using FileStream fs = new(outputPath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

    private void overlayCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (btnManualOcr.IsChecked != true)
            return;

        if (lbPages.SelectedItem == null)
            return;

        _isDrawingSelection = true;
        _manualOcrStartPoint = e.GetPosition(overlayCanvas);
        overlayCanvas.CaptureMouse();

        _selectionRect = new Rectangle
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A2BE2")), // Tím neon viền đứt
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 138, 43, 226)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(_selectionRect, _manualOcrStartPoint.X);
        Canvas.SetTop(_selectionRect, _manualOcrStartPoint.Y);
        _selectionRect.Width = 0;
        _selectionRect.Height = 0;

        overlayCanvas.Children.Add(_selectionRect);
        e.Handled = true;
    }

    private void overlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingSelection || _selectionRect == null)
            return;

        Point currentPoint = e.GetPosition(overlayCanvas);

        double left = Math.Min(_manualOcrStartPoint.X, currentPoint.X);
        double top = Math.Min(_manualOcrStartPoint.Y, currentPoint.Y);
        double width = Math.Abs(_manualOcrStartPoint.X - currentPoint.X);
        double height = Math.Abs(_manualOcrStartPoint.Y - currentPoint.Y);

        // Giới hạn trong phạm vi canvas
        left = Math.Max(0, Math.Min(left, overlayCanvas.Width));
        top = Math.Max(0, Math.Min(top, overlayCanvas.Height));
        width = Math.Min(width, overlayCanvas.Width - left);
        height = Math.Min(height, overlayCanvas.Height - top);

        Canvas.SetLeft(_selectionRect, left);
        Canvas.SetTop(_selectionRect, top);
        _selectionRect.Width = width;
        _selectionRect.Height = height;

        e.Handled = true;
    }

    private async void overlayCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingSelection)
            return;

        _isDrawingSelection = false;
        overlayCanvas.ReleaseMouseCapture();

        if (_selectionRect == null)
            return;

        double left = Canvas.GetLeft(_selectionRect);
        double top = Canvas.GetTop(_selectionRect);
        double width = _selectionRect.Width;
        double height = _selectionRect.Height;

        overlayCanvas.Children.Remove(_selectionRect);
        _selectionRect = null;
        e.Handled = true;

        if (width < 5 || height < 5)
        {
            return; // Vùng chọn quá nhỏ
        }

        if (_viewModel.SelectedPage is not PageItem selectedPage)
            return;

        try
        {
            _viewModel.StatusText = "Đang quét vùng chọn...";
            _viewModel.IsProcessing = true;
            _viewModel.IsProgressIndeterminate = true;
            btnManualOcr.IsEnabled = false;

            // 1. Cắt ảnh (Crop) trên C# client
            byte[] croppedBytes = CropImage(_currentImagePath ?? selectedPage.ImagePath, (int)left, (int)top, (int)width, (int)height);

            // 2. Gửi nhận diện OCR lên server
            string lang = _viewModel.SelectedSourceLanguage;
            var results = await _ocrService.RecognizeBytesAsync(croppedBytes, lang);

            if (results == null || results.Count == 0)
            {
                _viewModel.StatusText = "Không tìm thấy chữ trong vùng chọn.";
                return;
            }

            // 3. Tịnh tiến tọa độ kết quả để khớp trên ảnh gốc toàn trang
            foreach (var item in results)
            {
                foreach (var pt in item.Box)
                {
                    pt[0] += left;
                    pt[1] += top;
                }
            }

            // 4. Cộng dồn kết quả OCR vào trang
            if (selectedPage.OcrResults == null)
            {
                selectedPage.OcrResults = new List<OcrResult>();
            }

            selectedPage.OcrResults.AddRange(results);
            _viewModel.UpdateOcrResults();

            if (selectedPage.IsReplaced)
            {
                DrawTranslatedText(selectedPage);
            }
            else
            {
                DrawOcrBoxes(selectedPage);
            }

            _viewModel.StatusText = $"Đã quét và thêm {results.Count} vùng văn bản mới.";
        }
        catch (Exception ex)
        {
            _viewModel.ShowMessage?.Invoke($"Lỗi quét OCR thủ công: {ex.Message}", "Lỗi quét thủ công", true);
            _viewModel.StatusText = "Lỗi quét thủ công.";
        }
        finally
        {
            _viewModel.IsProcessing = false;
            _viewModel.IsProgressIndeterminate = false;
            btnManualOcr.IsEnabled = true;
        }
    }

    private byte[] CropImage(string imagePath, int x, int y, int width, int height)
    {
        BitmapDecoder decoder = BitmapDecoder.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];

        // Ràng buộc tọa độ trong lòng ảnh
        x = Math.Clamp(x, 0, frame.PixelWidth);
        y = Math.Clamp(y, 0, frame.PixelHeight);
        width = Math.Clamp(width, 1, frame.PixelWidth - x);
        height = Math.Clamp(height, 1, frame.PixelHeight - y);

        CroppedBitmap cropped = new(frame, new Int32Rect(x, y, width, height));

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        
        using MemoryStream ms = new();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private void lvOcrResult_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Tránh xung đột với TextBox đang soạn thảo chữ
        if (e.OriginalSource is DependencyObject depObj)
        {
            while (depObj != null && depObj is not ListView)
            {
                if (depObj is TextBox)
                {
                    return; // Nhấp vào TextBox -> Cho phép gõ chữ/chọn text, không kích hoạt kéo thả
                }
                depObj = VisualTreeHelper.GetParent(depObj);
            }
        }

        _ocrDragStartPoint = e.GetPosition(null);
        _ocrDraggedItem = GetListViewItemAtPoint<OcrResult>(lvOcrResult, e.GetPosition(lvOcrResult));
    }

    private void lvOcrResult_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _ocrDraggedItem != null)
        {
            Point mousePos = e.GetPosition(null);
            Vector diff = _ocrDragStartPoint - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(lvOcrResult, _ocrDraggedItem, DragDropEffects.Move);
            }
        }
    }

    private void lvOcrResult_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(OcrResult)))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void lvOcrResult_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(OcrResult)))
        {
            OcrResult? droppedItem = e.Data.GetData(typeof(OcrResult)) as OcrResult;
            OcrResult? targetItem = GetListViewItemAtPoint<OcrResult>(lvOcrResult, e.GetPosition(lvOcrResult));

            if (droppedItem != null && targetItem != null && droppedItem != targetItem)
            {
                if (_viewModel.SelectedPage is PageItem selectedPage)
                {
                    int oldIndex = selectedPage.OcrResults.IndexOf(droppedItem);
                    int newIndex = selectedPage.OcrResults.IndexOf(targetItem);

                    if (oldIndex >= 0 && newIndex >= 0)
                    {
                        selectedPage.OcrResults.RemoveAt(oldIndex);
                        selectedPage.OcrResults.Insert(newIndex, droppedItem);

                        _viewModel.UpdateOcrResults();

                        // Vẽ lại Canvas để khớp số thứ tự hoặc hiển thị text dịch tương ứng
                        if (selectedPage.IsReplaced)
                        {
                            DrawTranslatedText(selectedPage);
                        }
                        else
                        {
                            DrawOcrBoxes(selectedPage);
                        }
                    }
                }
            }
        }
    }

    private T? GetListViewItemAtPoint<T>(ListView listView, Point point) where T : class
    {
        HitTestResult hitTestResult = VisualTreeHelper.HitTest(listView, point);
        DependencyObject? obj = hitTestResult?.VisualHit;
        while (obj != null && obj != listView)
        {
            if (obj is ListViewItem listViewItem)
            {
                return listViewItem.DataContext as T;
            }
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private void StopOcrService()
    {
        if (_ocrProcess != null && !_ocrProcess.HasExited)
        {
            try
            {
                _ocrProcess.Kill(true); // Kill toàn bộ cây tiến trình (bao gồm các tiến trình con của Flask)
                _ocrProcess.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to kill OCR process: {ex.Message}");
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopOcrService();
        base.OnClosed(e);
    }

    private void UpdateZoom(double newScale)
    {
        _zoomScale = Math.Clamp(newScale, MinScale, MaxScale);
        if (imgScaleTransform != null)
        {
            imgScaleTransform.ScaleX = _zoomScale;
            imgScaleTransform.ScaleY = _zoomScale;
        }
        if (txtZoomPercent != null)
        {
            txtZoomPercent.Text = $"{Math.Round(_zoomScale * 100)}%";
        }
    }

    private void FitToScreen()
    {
        if (imgComic == null || imgComic.Source is not BitmapSource bitmap || imageScrollViewer == null)
            return;

        double viewportWidth = imageScrollViewer.ActualWidth;
        double viewportHeight = imageScrollViewer.ActualHeight;

        if (viewportWidth == 0 || viewportHeight == 0)
        {
            UpdateZoom(1.0);
            return;
        }

        double margin = 10;
        double scaleX = (viewportWidth - margin) / bitmap.PixelWidth;
        double scaleY = (viewportHeight - margin) / bitmap.PixelHeight;
        double scale = Math.Min(scaleX, scaleY);

        UpdateZoom(scale);
    }

    private void ZoomAtPoint(double newScale, Point mousePosInScrollViewer, Point mousePosInContent)
    {
        double oldScale = _zoomScale;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);

        if (Math.Abs(newScale - oldScale) < 0.001)
            return;

        UpdateZoom(newScale);

        imageScrollViewer.UpdateLayout();

        double scaleRatio = newScale / oldScale;
        double newHOffset = mousePosInContent.X * scaleRatio - mousePosInScrollViewer.X;
        double newVOffset = mousePosInContent.Y * scaleRatio - mousePosInScrollViewer.Y;

        imageScrollViewer.ScrollToHorizontalOffset(newHOffset);
        imageScrollViewer.ScrollToVerticalOffset(newVOffset);
    }

    private void ZoomAtCenter(double zoomFactor)
    {
        double oldScale = _zoomScale;
        double newScale = oldScale * zoomFactor;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);

        if (Math.Abs(newScale - oldScale) < 0.001)
            return;

        Point scrollViewerCenter = new Point(imageScrollViewer.ActualWidth / 2, imageScrollViewer.ActualHeight / 2);
        Point contentCenter = new Point(
            imageScrollViewer.HorizontalOffset + scrollViewerCenter.X,
            imageScrollViewer.VerticalOffset + scrollViewerCenter.Y
        );
        Point unscaledCenter = new Point(contentCenter.X / oldScale, contentCenter.Y / oldScale);

        UpdateZoom(newScale);

        imageScrollViewer.UpdateLayout();

        double newHOffset = unscaledCenter.X * newScale - scrollViewerCenter.X;
        double newVOffset = unscaledCenter.Y * newScale - scrollViewerCenter.Y;

        imageScrollViewer.ScrollToHorizontalOffset(newHOffset);
        imageScrollViewer.ScrollToVerticalOffset(newVOffset);
    }

    private void imageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            Point mousePosInScrollViewer = e.GetPosition(imageScrollViewer);
            Point mousePosInContent = e.GetPosition(imageContainer);
            ZoomAtPoint(_zoomScale * zoomFactor, mousePosInScrollViewer, mousePosInContent);
            e.Handled = true;
        }
    }

    private void imageScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Detect double click to Fit to Screen
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            if (e.OriginalSource is DependencyObject depObj)
            {
                bool overInteractive = false;
                while (depObj != null && depObj != imageScrollViewer)
                {
                    if (depObj is TextBox || depObj is Rectangle || depObj is ContextMenu || depObj is Button)
                    {
                        overInteractive = true;
                        break;
                    }
                    depObj = VisualTreeHelper.GetParent(depObj);
                }
                if (!overInteractive)
                {
                    if (_isPanning)
                    {
                        imageScrollViewer.ReleaseMouseCapture();
                        _isPanning = false;
                        imageScrollViewer.Cursor = Cursors.Arrow;
                    }
                    FitToScreen();
                    e.Handled = true;
                    return;
                }
            }
        }

        bool isMiddleButton = e.ChangedButton == MouseButton.Middle && e.MiddleButton == MouseButtonState.Pressed;
        bool isLeftButton = e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed && btnManualOcr.IsChecked != true;

        if (isMiddleButton || isLeftButton)
        {
            if (e.OriginalSource is DependencyObject depObj)
            {
                while (depObj != null && depObj != imageScrollViewer)
                {
                    if (depObj is TextBox || depObj is Rectangle || depObj is ContextMenu || depObj is Button)
                    {
                        return;
                    }
                    depObj = VisualTreeHelper.GetParent(depObj);
                }
            }

            _isPanning = true;
            _panStartMousePosition = e.GetPosition(imageScrollViewer);
            _panStartScrollOffset = new Point(imageScrollViewer.HorizontalOffset, imageScrollViewer.VerticalOffset);
            imageScrollViewer.CaptureMouse();
            imageScrollViewer.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void imageScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            Point currentPos = e.GetPosition(imageScrollViewer);
            Vector delta = currentPos - _panStartMousePosition;

            imageScrollViewer.ScrollToHorizontalOffset(_panStartScrollOffset.X - delta.X);
            imageScrollViewer.ScrollToVerticalOffset(_panStartScrollOffset.Y - delta.Y);
            e.Handled = true;
        }
    }

    private void imageScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            imageScrollViewer.ReleaseMouseCapture();
            _isPanning = false;
            imageScrollViewer.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void imageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isFirstLayout && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _isFirstLayout = false;
            FitToScreen();
        }
    }

    private void overlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            FitToScreen();
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                ZoomAtCenter(1.2);
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                ZoomAtCenter(0.8);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                FitToScreen();
                e.Handled = true;
            }
        }
    }

    private void btnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomAtCenter(1.2);
    }

    private void btnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomAtCenter(0.8);
    }

    private void btnZoomFit_Click(object sender, RoutedEventArgs e)
    {
        FitToScreen();
    }
}