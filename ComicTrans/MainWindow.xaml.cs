using ComicTrans.Models;
using ComicTrans.Services;
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

    private List<OcrResult> _ocrResults = new();

    private readonly List<Rectangle> _rectangles = new();

    private string? _currentImagePath;

    private readonly ObservableCollection<PageItem> _pages = new();
    private Point _dragStartPoint;
    private PageItem? _draggedItem;
    private System.Diagnostics.Process? _ocrProcess;

    public MainWindow()
    {
        InitializeComponent();
        lbPages.ItemsSource = _pages;
        StartOcrService();
    }

    private void btnOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new()
        {
            Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp",
            Multiselect = true
        };

        if (dlg.ShowDialog() != true)
            return;

        if (_pages.Count + dlg.FileNames.Length > 30)
        {
            MessageBox.Show($"Tổng số trang vượt quá giới hạn 30 trang (Hiện có: {_pages.Count}, Số trang thêm mới: {dlg.FileNames.Length}). Vui lòng chọn ít ảnh hơn.");
            return;
        }

        int firstNewIndex = _pages.Count;

        foreach (var file in dlg.FileNames)
        {
            var thumbnail = CreateThumbnail(file);
            _pages.Add(new PageItem
            {
                ImagePath = file,
                PageName = System.IO.Path.GetFileName(file),
                Thumbnail = thumbnail
            });
        }

        UpdatePageNumbers();

        // Tự động di chuyển chọn tới trang mới được nạp đầu tiên
        lbPages.SelectedIndex = firstNewIndex;
    }

    private void btnNew_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count > 0)
        {
            var result = MessageBox.Show(
                "Bạn có chắc chắn muốn xóa danh sách trang hiện tại để làm mới không?",
                "Bắt đầu dự án mới",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        _pages.Clear();
        _ocrResults = new List<OcrResult>();
        lvOcrResult.ItemsSource = null;
        overlayCanvas.Children.Clear();
        _rectangles.Clear();
        imgComic.Source = null;
        _currentImagePath = null;
        txtStatus.Text = "Đã làm mới danh sách trang.";
    }

    private ImageSource CreateThumbnail(string path)
    {
        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 100;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create thumbnail: {ex.Message}");
            return new BitmapImage();
        }
    }

    private void UpdatePageNumbers()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].PageNumber = i + 1;
        }
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0)
        {
            MessageBox.Show("Vui lòng mở ảnh trước.");
            return;
        }

        try
        {
            btnOCR.IsEnabled = false;
            btnTranslate.IsEnabled = false;
            btnOpen.IsEnabled = false;
            pbProgress.Visibility = Visibility.Visible;
            pbProgress.Maximum = _pages.Count;
            pbProgress.Value = 0;

            List<string> failedPages = new();
            string lang = GetSelectedSourceLanguage();

            for (int i = 0; i < _pages.Count; i++)
            {
                var page = _pages[i];
                pbProgress.Value = i;

                // Nếu trang đã có kết quả OCR thành công trước đó, bỏ qua không quét lại
                if (page.OcrResults != null && page.OcrResults.Count > 0)
                {
                    continue;
                }

                txtStatus.Text = $"Đang nhận diện chữ (OCR) trang {i + 1}/{_pages.Count}: {page.PageName}...";

                try
                {
                    var results = await _ocrService.RecognizeAsync(page.ImagePath, lang);
                    page.OcrResults = results;
                    page.IsTranslated = false; // Reset trạng thái dịch vì chữ vừa quét mới
                    page.IsReplaced = false;
                    page.CleanImagePath = null;

                    // Nếu trang đang xử lý trùng khớp với trang đang được chọn, cập nhật UI
                    if (lbPages.SelectedItem == page)
                    {
                        _ocrResults = results;
                        DrawOcrBoxes();
                        lvOcrResult.ItemsSource = _ocrResults;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to OCR page {page.PageName}: {ex.Message}");
                    failedPages.Add(page.PageName);
                }
            }

            pbProgress.Value = _pages.Count;

            if (failedPages.Count > 0)
            {
                txtStatus.Text = $"Nhận diện chữ hoàn tất. Lỗi ở {failedPages.Count} trang.";
                MessageBox.Show($"Đã hoàn thành nhận diện chữ (OCR).\n\nCó {failedPages.Count} trang gặp lỗi không thể nhận diện chữ:\n" + string.Join("\n", failedPages));
            }
            else
            {
                txtStatus.Text = "Nhận diện chữ (OCR) hoàn thành cho tất cả các trang.";
                MessageBox.Show("Đã hoàn thành nhận diện chữ (OCR) cho tất cả các trang.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi không xác định trong quá trình OCR: {ex.Message}");
            txtStatus.Text = "Lỗi nhận diện chữ (OCR).";
        }
        finally
        {
            btnOCR.IsEnabled = true;
            btnTranslate.IsEnabled = true;
            btnOpen.IsEnabled = true;
            pbProgress.Visibility = Visibility.Collapsed;
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

    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is OcrResult item)
        {
            lvOcrResult.SelectedItem = item;
        }
    }

    private async void btnTranslate_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0)
        {
            MessageBox.Show("Vui lòng mở ảnh và thực hiện OCR trước.");
            return;
        }

        bool hasOcr = _pages.Any(p => p.OcrResults != null && p.OcrResults.Count > 0);
        if (!hasOcr)
        {
            MessageBox.Show("Vui lòng thực hiện OCR trước khi dịch.");
            return;
        }

        if (!_translationService.IsKeyConfigured())
        {
            MessageBox.Show("Vui lòng cấu hình Gemini API Key trong file config.json ở thư mục chạy ứng dụng trước khi dịch.");
            return;
        }

        try
        {
            btnOCR.IsEnabled = false;
            btnTranslate.IsEnabled = false;
            btnOpen.IsEnabled = false;
            pbProgress.Visibility = Visibility.Visible;
            pbProgress.Maximum = _pages.Count;
            pbProgress.Value = 0;

            List<string> failedPages = new();

            for (int i = 0; i < _pages.Count; i++)
            {
                var page = _pages[i];
                if (page.OcrResults == null || page.OcrResults.Count == 0)
                {
                    continue;
                }

                pbProgress.Value = i;

                // Nếu trang đã dịch thành công trước đó, bỏ qua không dịch lại
                if (page.IsTranslated)
                {
                    continue;
                }

                txtStatus.Text = $"Đang dịch trang {i + 1}/{_pages.Count}: {page.PageName}...";

                try
                {
                    var originalTexts = page.OcrResults.Select(r => r.Text).ToList();
                    var translatedTexts = await _translationService.TranslateBatchAsync(originalTexts);

                    for (int j = 0; j < Math.Min(page.OcrResults.Count, translatedTexts.Count); j++)
                    {
                        page.OcrResults[j].Text = translatedTexts[j];
                    }

                    page.IsTranslated = true; // Đánh dấu dịch thành công

                    // Nếu trang đang dịch trùng khớp với trang đang được chọn, cập nhật UI
                    if (lbPages.SelectedItem == page)
                    {
                        _ocrResults = page.OcrResults;
                        lvOcrResult.ItemsSource = null;
                        lvOcrResult.ItemsSource = _ocrResults;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to translate page {page.PageName}: {ex.Message}");
                    failedPages.Add(page.PageName);
                }
            }

            pbProgress.Value = _pages.Count;

            if (failedPages.Count > 0)
            {
                txtStatus.Text = $"Dịch hoàn tất. Lỗi ở {failedPages.Count} trang.";
                MessageBox.Show($"Đã hoàn thành dịch thuật.\n\nCó {failedPages.Count} trang gặp lỗi không thể dịch:\n" + string.Join("\n", failedPages));
            }
            else
            {
                txtStatus.Text = "Dịch thuật hoàn thành cho tất cả các trang.";
                MessageBox.Show("Đã hoàn thành dịch thuật cho tất cả các trang.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi không xác định khi dịch: {ex.Message}");
            txtStatus.Text = "Lỗi dịch thuật.";
        }
        finally
        {
            btnOCR.IsEnabled = true;
            btnTranslate.IsEnabled = true;
            btnOpen.IsEnabled = true;
            pbProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void lbPages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lbPages.SelectedItem is PageItem selectedPage)
        {
            LoadPage(selectedPage);
        }
        else
        {
            _currentImagePath = null;
            imgComic.Source = null;
            overlayCanvas.Children.Clear();
            _rectangles.Clear();
            _ocrResults = new List<OcrResult>();
            lvOcrResult.ItemsSource = null;
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

        _ocrResults = page.OcrResults;
        lvOcrResult.ItemsSource = _ocrResults;

        if (_ocrResults != null && _ocrResults.Count > 0)
        {
            if (page.IsReplaced)
            {
                DrawTranslatedText();
            }
            else
            {
                DrawOcrBoxes();
            }
        }
    }

    private void DrawTranslatedText()
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
                AcceptsReturn = true,
                Padding = new Thickness(2),
                Margin = new Thickness(0)
            };

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

            // Viền chữ bằng hiệu ứng bóng mờ màu trắng để tăng độ tương phản trên nền tối
            var outlineEffect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.White,
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

    private async void btnReplace_Click(object sender, RoutedEventArgs e)
    {
        if (lbPages.SelectedItem is not PageItem selectedPage)
        {
            MessageBox.Show("Vui lòng mở ảnh trước.");
            return;
        }

        if (selectedPage.OcrResults == null || selectedPage.OcrResults.Count == 0)
        {
            MessageBox.Show("Trang này chưa được quét OCR. Vui lòng quét OCR và dịch trước khi thay thế.");
            return;
        }

        // Nếu đã thay thế rồi, bấm lại sẽ tắt chế độ thay thế
        if (selectedPage.IsReplaced)
        {
            selectedPage.IsReplaced = false;
            LoadPage(selectedPage);
            txtStatus.Text = "Đã tắt chế độ thay thế.";
            return;
        }

        try
        {
            btnReplace.IsEnabled = false;
            pbProgress.Visibility = Visibility.Visible;
            pbProgress.IsIndeterminate = true;
            txtStatus.Text = "Đang thực hiện xóa chữ và thay thế bằng chữ dịch...";

            // Thu thập các bounding box
            var boxes = new List<List<List<double>>>();
            foreach (var result in selectedPage.OcrResults)
            {
                boxes.Add(result.Box);
            }

            // Gọi API inpaint
            byte[] cleanImageBytes = await _ocrService.InpaintAsync(selectedPage.ImagePath, boxes);

            // Lưu file tạm sạch chữ
            string dir = System.IO.Path.GetDirectoryName(selectedPage.ImagePath) ?? "";
            string fileName = System.IO.Path.GetFileNameWithoutExtension(selectedPage.ImagePath) + "_clean.png";
            string cleanPath = System.IO.Path.Combine(dir, fileName);

            await File.WriteAllBytesAsync(cleanPath, cleanImageBytes);

            selectedPage.CleanImagePath = cleanPath;
            selectedPage.IsReplaced = true;

            // Nạp lại trang hiển thị mới
            LoadPage(selectedPage);
            txtStatus.Text = "Thay thế chữ hoàn tất. Mẹo: Cuộn chuột trên chữ dịch để phóng to/thu nhỏ.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi thực hiện thay thế chữ: {ex.Message}");
            txtStatus.Text = "Lỗi thay thế chữ.";
        }
        finally
        {
            btnReplace.IsEnabled = true;
            pbProgress.Visibility = Visibility.Collapsed;
            pbProgress.IsIndeterminate = false;
        }
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
                int oldIndex = _pages.IndexOf(droppedItem);
                int newIndex = _pages.IndexOf(targetItem);

                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _pages.Move(oldIndex, newIndex);
                    UpdatePageNumbers();
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

    private async void MenuForceOcr_Click(object sender, RoutedEventArgs e)
    {
        if (lbPages.SelectedItem is PageItem selectedPage)
        {
            try
            {
                txtStatus.Text = $"Đang nhận diện lại chữ (Force OCR) cho: {selectedPage.PageName}...";
                string lang = GetSelectedSourceLanguage();
                var results = await _ocrService.RecognizeAsync(selectedPage.ImagePath, lang);
                selectedPage.OcrResults = results;
                selectedPage.IsTranslated = false; // Reset trạng thái dịch
                selectedPage.IsReplaced = false;
                selectedPage.CleanImagePath = null;

                _ocrResults = results;
                DrawOcrBoxes();
                lvOcrResult.ItemsSource = _ocrResults;

                txtStatus.Text = $"Đã quét lại thành công trang: {selectedPage.PageName}";
                MessageBox.Show($"Đã quét lại thành công trang: {selectedPage.PageName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi quét lại trang {selectedPage.PageName}: {ex.Message}");
                txtStatus.Text = "Lỗi quét lại trang.";
            }
        }
    }

    private async void MenuForceTranslate_Click(object sender, RoutedEventArgs e)
    {
        if (lbPages.SelectedItem is PageItem selectedPage)
        {
            if (selectedPage.OcrResults == null || selectedPage.OcrResults.Count == 0)
            {
                MessageBox.Show("Trang này chưa được quét OCR. Vui lòng quét OCR trước.");
                return;
            }

            if (!_translationService.IsKeyConfigured())
            {
                MessageBox.Show("Vui lòng cấu hình Gemini API Key trong file config.json.");
                return;
            }

            try
            {
                txtStatus.Text = $"Đang dịch lại (Force Translate) cho: {selectedPage.PageName}...";
                var originalTexts = selectedPage.OcrResults.Select(r => r.Text).ToList();
                var translatedTexts = await _translationService.TranslateBatchAsync(originalTexts);

                for (int i = 0; i < Math.Min(selectedPage.OcrResults.Count, translatedTexts.Count); i++)
                {
                    selectedPage.OcrResults[i].Text = translatedTexts[i];
                }

                selectedPage.IsTranslated = true;

                _ocrResults = selectedPage.OcrResults;
                lvOcrResult.ItemsSource = null;
                lvOcrResult.ItemsSource = _ocrResults;

                txtStatus.Text = $"Đã dịch lại thành công trang: {selectedPage.PageName}";
                MessageBox.Show($"Đã dịch lại thành công trang: {selectedPage.PageName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi dịch lại trang {selectedPage.PageName}: {ex.Message}");
                txtStatus.Text = "Lỗi dịch lại trang.";
            }
        }
    }

    private void MenuClearResults_Click(object sender, RoutedEventArgs e)
    {
        if (lbPages.SelectedItem is PageItem selectedPage)
        {
            selectedPage.OcrResults = new List<OcrResult>();
            selectedPage.IsTranslated = false;
            selectedPage.IsReplaced = false;
            selectedPage.CleanImagePath = null;
            
            if (lbPages.SelectedItem == selectedPage)
            {
                _ocrResults = selectedPage.OcrResults;
                overlayCanvas.Children.Clear();
                _rectangles.Clear();
                lvOcrResult.ItemsSource = null;
            }
            txtStatus.Text = $"Đã xóa kết quả của trang: {selectedPage.PageName}";
        }
    }

    private void MenuDeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (lbPages.SelectedItem is PageItem selectedPage)
        {
            var result = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa trang '{selectedPage.PageName}' khỏi danh sách không?",
                "Xóa trang",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int selectedIndex = _pages.IndexOf(selectedPage);
            _pages.Remove(selectedPage);

            UpdatePageNumbers();

            if (_pages.Count > 0)
            {
                int nextSelectIndex = Math.Max(0, selectedIndex - 1);
                if (nextSelectIndex < _pages.Count)
                {
                    lbPages.SelectedIndex = nextSelectIndex;
                }
                else
                {
                    lbPages.SelectedIndex = 0;
                }
            }
            else
            {
                txtStatus.Text = "Danh sách trang trống.";
            }
        }
    }

    private string GetSelectedSourceLanguage()
    {
        if (cbSourceLang.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string tag)
        {
            return tag;
        }
        return "en";
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

    private async void btnExport_Click(object sender, RoutedEventArgs e)
    {
        var replacedPages = _pages.Where(p => p.IsReplaced && !string.IsNullOrEmpty(p.CleanImagePath) && File.Exists(p.CleanImagePath)).ToList();

        if (replacedPages.Count == 0)
        {
            MessageBox.Show("Không tìm thấy trang nào đã được thực hiện 'Thay thế' để xuất. Vui lòng bấm nút 'Thay thế' ở các trang bạn muốn xuất trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmResult = MessageBox.Show(
            $"Bạn có chắc chắn muốn xuất toàn bộ {replacedPages.Count} trang ảnh đã được thay thế chữ dịch không?",
            "Xác nhận xuất ảnh",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes)
            return;

        var folderDlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Chọn thư mục xuất ảnh đã dịch"
        };

        if (folderDlg.ShowDialog() != true)
            return;

        string outputDir = folderDlg.FolderName;

        try
        {
            btnExport.IsEnabled = false;
            pbProgress.Visibility = Visibility.Visible;
            pbProgress.Maximum = replacedPages.Count;
            pbProgress.Value = 0;
            Mouse.OverrideCursor = Cursors.Wait;

            for (int i = 0; i < replacedPages.Count; i++)
            {
                var page = replacedPages[i];
                txtStatus.Text = $"Đang xuất ảnh {i + 1}/{replacedPages.Count}: {page.PageName}...";
                pbProgress.Value = i;

                string outputFileName = System.IO.Path.ChangeExtension(page.PageName, ".png");
                string outputPath = System.IO.Path.Combine(outputDir, outputFileName);
                
                // Thực thi tác vụ render trên luồng giao diện (STA thread)
                await Task.Run(() => 
                {
                    Dispatcher.Invoke(() =>
                    {
                        SaveReplacedImage(page, outputPath);
                    });
                });
            }

            pbProgress.Value = replacedPages.Count;
            txtStatus.Text = $"Đã xuất thành công {replacedPages.Count} ảnh vào thư mục: {outputDir}";
            MessageBox.Show($"Đã xuất thành công {replacedPages.Count} ảnh đã được dịch!", "Xuất ảnh hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi trong quá trình xuất ảnh: {ex.Message}", "Lỗi xuất ảnh", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Lỗi xuất ảnh.";
        }
        finally
        {
            btnExport.IsEnabled = true;
            pbProgress.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
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
                Text = item.Text,
                Padding = new Thickness(2),
                Margin = new Thickness(0)
            };

            // Sử dụng font size tùy biến nếu có, ngược lại tự động tính cỡ chữ tối ưu
            textBox.FontSize = item.FontSize > 0 ? item.FontSize : CalculateOptimalFontSize(item.Text, w, h);

            // Hiệu ứng bóng mờ viền chữ trắng
            var outlineEffect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.White,
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
}