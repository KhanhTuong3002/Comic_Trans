using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComicTrans.Helpers;
using ComicTrans.Models;
using ComicTrans.Services;

namespace ComicTrans.ViewModels
{
    /// <summary>
    /// ViewModel chính của ứng dụng ComicTrans.
    /// Quản lý dữ liệu trang truyện, kết quả OCR, trạng thái tiến trình và các lệnh điều hướng chính.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        // Các dịch vụ kết nối ngoài
        private readonly OcrService _ocrService = new();
        private readonly TranslationService _translationService = new();

        // Danh sách trang truyện hiển thị ở panel bên trái
        private ObservableCollection<PageItem> _pages = new();
        public ObservableCollection<PageItem> Pages
        {
            get => _pages;
            set => SetProperty(ref _pages, value);
        }

        // Trang truyện đang được chọn hiện tại
        private PageItem? _selectedPage;
        public PageItem? SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (SetProperty(ref _selectedPage, value))
                {
                    // Cập nhật danh sách kết quả OCR tương ứng với trang được chọn
                    UpdateOcrResults();
                }
            }
        }

        // Danh sách kết quả OCR hiển thị ở panel bên phải
        private ObservableCollection<OcrResult> _ocrResults = new();
        public ObservableCollection<OcrResult> OcrResults
        {
            get => _ocrResults;
            set => SetProperty(ref _ocrResults, value);
        }

        // Kết quả OCR đang được chọn từ danh sách
        private OcrResult? _selectedOcrResult;
        public OcrResult? SelectedOcrResult
        {
            get => _selectedOcrResult;
            set => SetProperty(ref _selectedOcrResult, value);
        }

        // Ngôn ngữ nguồn để quét OCR
        private string _selectedSourceLanguage = "en";
        public string SelectedSourceLanguage
        {
            get => _selectedSourceLanguage;
            set => SetProperty(ref _selectedSourceLanguage, value);
        }

        // Dòng chữ hiển thị trạng thái ở dưới thanh StatusBar
        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // Giá trị tiến trình hiện tại của ProgressBar
        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        // Giá trị tối đa của ProgressBar (Tổng số trang)
        private double _progressMaximum = 100;
        public double ProgressMaximum
        {
            get => _progressMaximum;
            set => SetProperty(ref _progressMaximum, value);
        }

        // Trạng thái ứng dụng đang bận thực hiện tác vụ (OCR, dịch...)
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        // Trạng thái hiển thị vòng xoay chờ vô hạn (Indeterminate)
        private bool _isProgressIndeterminate;
        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        #region Các Delegate giao tiếp với View (Giúp giải耦 hoàn toàn UI và Logic)
        public Func<string, List<string>?>? RequestFilesSelection { get; set; }
        public Func<string, string?>? RequestFolderSelection { get; set; }
        public Func<string, string, string, bool>? ShowConfirmDialog { get; set; } // (message, title, tag)
        public Action<string, string, bool>? ShowMessage { get; set; } // (message, title, isError)
        public Func<PageItem, string, Task>? SaveImageDelegate { get; set; }
        #endregion

        #region Các Lệnh điều khiển (Commands)
        public ICommand OpenImagesCommand { get; }
        public ICommand NewProjectCommand { get; }
        public ICommand RunOcrCommand { get; }
        public ICommand TranslateCommand { get; }
        public ICommand ReplaceCommand { get; }
        public ICommand ExportCommand { get; }
        
        // Lệnh menu chuột phải danh sách trang
        public ICommand ForceOcrCommand { get; }
        public ICommand ForceTranslateCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand DeletePageCommand { get; }
        public ICommand DeleteOcrCommand { get; }
        #endregion

        public MainViewModel()
        {
            // Đăng ký các lệnh
            OpenImagesCommand = new RelayCommand(OpenImages, () => !IsProcessing);
            NewProjectCommand = new RelayCommand(NewProject, () => !IsProcessing);
            RunOcrCommand = new RelayCommand(async () => await RunOcrAsync(), () => !IsProcessing && Pages.Count > 0);
            TranslateCommand = new RelayCommand(async () => await TranslateAsync(), () => !IsProcessing && Pages.Count > 0);
            ReplaceCommand = new RelayCommand(async () => await ReplaceAsync(), () => !IsProcessing && SelectedPage != null);
            ExportCommand = new RelayCommand(async () => await ExportAsync(), () => !IsProcessing && Pages.Count > 0);

            ForceOcrCommand = new RelayCommand(async () => await ForceOcrAsync(), () => !IsProcessing && SelectedPage != null);
            ForceTranslateCommand = new RelayCommand(async () => await ForceTranslateAsync(), () => !IsProcessing && SelectedPage != null);
            ClearResultsCommand = new RelayCommand(ClearResults, () => !IsProcessing && SelectedPage != null);
            DeletePageCommand = new RelayCommand(DeletePage, () => !IsProcessing && SelectedPage != null);
            DeleteOcrCommand = new RelayCommand<OcrResult>(DeleteOcr, (ocr) => !IsProcessing && SelectedPage != null && ocr != null);
        }

        /// <summary>
        /// Đồng bộ danh sách kết quả OCR khi trang được chọn thay đổi.
        /// </summary>
        public void UpdateOcrResults()
        {
            if (SelectedPage != null)
            {
                OcrResults = new ObservableCollection<OcrResult>(SelectedPage.OcrResults);
            }
            else
            {
                OcrResults = new ObservableCollection<OcrResult>();
            }
        }

        /// <summary>
        /// Mở hộp thoại chọn ảnh và nạp vào danh sách các trang.
        /// </summary>
        private void OpenImages()
        {
            if (RequestFilesSelection == null) return;

            var files = RequestFilesSelection("Image|*.png;*.jpg;*.jpeg;*.bmp;*.webp");
            if (files == null || files.Count == 0) return;

            if (Pages.Count + files.Count > 30)
            {
                ShowMessage?.Invoke(
                    $"Tổng số trang vượt quá giới hạn 30 trang (Hiện có: {Pages.Count}, Số trang thêm mới: {files.Count}). Vui lòng chọn ít ảnh hơn.",
                    "Giới hạn số trang",
                    false);
                return;
            }

            int firstNewIndex = Pages.Count;

            foreach (var file in files)
            {
                var thumbnail = CreateThumbnail(file);
                Pages.Add(new PageItem
                {
                    ImagePath = file,
                    PageName = Path.GetFileName(file),
                    Thumbnail = thumbnail
                });
            }

            UpdatePageNumbers();

            // Tự động di chuyển chọn tới trang mới được nạp đầu tiên
            if (firstNewIndex < Pages.Count)
            {
                SelectedPage = Pages[firstNewIndex];
            }

            StatusText = $"Đã tải thêm {files.Count} trang ảnh.";
        }

        /// <summary>
        /// Khởi động lại dự án mới, xóa sạch dữ liệu.
        /// </summary>
        private void NewProject()
        {
            if (Pages.Count > 0)
            {
                bool? confirm = ShowConfirmDialog?.Invoke(
                    "Bạn có chắc chắn muốn xóa danh sách trang hiện tại để làm mới không?",
                    "Bắt đầu dự án mới",
                    "New");

                if (confirm != true) return;
            }

            Pages.Clear();
            OcrResults.Clear();
            SelectedPage = null;
            StatusText = "Đã làm mới danh sách trang.";
        }

        /// <summary>
        /// Thực hiện nhận diện chữ (OCR) trên tất cả các trang chưa được quét.
        /// </summary>
        private async Task RunOcrAsync()
        {
            if (Pages.Count == 0) return;

            try
            {
                IsProcessing = true;
                IsProgressIndeterminate = false;
                ProgressMaximum = Pages.Count;
                ProgressValue = 0;

                List<string> failedPages = new();
                string lang = SelectedSourceLanguage;

                for (int i = 0; i < Pages.Count; i++)
                {
                    var page = Pages[i];
                    ProgressValue = i;

                    // Nếu trang đã có kết quả OCR thành công trước đó, bỏ qua không quét lại
                    if (page.OcrResults != null && page.OcrResults.Count > 0)
                    {
                        continue;
                    }

                    StatusText = $"Đang nhận diện chữ (OCR) trang {i + 1}/{Pages.Count}: {page.PageName}...";

                    try
                    {
                        var results = await _ocrService.RecognizeAsync(page.ImagePath, lang);
                        page.OcrResults = results;
                        page.IsTranslated = false; // Reset trạng thái dịch vì chữ vừa quét mới
                        page.IsReplaced = false;
                        page.CleanImagePath = null;

                        // Nếu đang ở trang được chọn, cập nhật danh sách hiển thị
                        if (SelectedPage == page)
                        {
                            UpdateOcrResults();
                            OnPropertyChanged(nameof(SelectedPage)); // Phát tín hiệu vẽ lại Box bên View
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to OCR page {page.PageName}: {ex.Message}");
                        failedPages.Add(page.PageName);
                    }
                }

                ProgressValue = Pages.Count;

                if (failedPages.Count > 0)
                {
                    StatusText = $"Nhận diện chữ hoàn tất. Lỗi ở {failedPages.Count} trang.";
                    ShowMessage?.Invoke(
                        $"Đã hoàn thành nhận diện chữ (OCR).\n\nCó {failedPages.Count} trang gặp lỗi không thể nhận diện chữ:\n" + string.Join("\n", failedPages),
                        "Kết quả OCR",
                        true);
                }
                else
                {
                    StatusText = "Nhận diện chữ (OCR) hoàn thành cho tất cả các trang.";
                    ShowMessage?.Invoke("Đã hoàn thành nhận diện chữ (OCR) cho tất cả các trang.", "Hoàn thành OCR", false);
                }
            }
            catch (Exception ex)
            {
                ShowMessage?.Invoke($"Lỗi không xác định trong quá trình OCR: {ex.Message}", "Lỗi OCR", true);
                StatusText = "Lỗi nhận diện chữ (OCR).";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// Dịch toàn bộ chữ của tất cả các trang đã được quét OCR.
        /// </summary>
        private async Task TranslateAsync()
        {
            if (Pages.Count == 0) return;

            bool hasOcr = Pages.Any(p => p.OcrResults != null && p.OcrResults.Count > 0);
            if (!hasOcr)
            {
                ShowMessage?.Invoke("Vui lòng thực hiện OCR trước khi dịch.", "Thông báo", false);
                return;
            }

            if (!_translationService.IsKeyConfigured())
            {
                ShowMessage?.Invoke("Vui lòng cấu hình Gemini API Key trong file config.json ở thư mục chạy ứng dụng trước khi dịch.", "Thiếu cấu hình API Key", true);
                return;
            }

            try
            {
                IsProcessing = true;
                IsProgressIndeterminate = false;
                ProgressMaximum = Pages.Count;
                ProgressValue = 0;

                List<string> failedPages = new();

                for (int i = 0; i < Pages.Count; i++)
                {
                    var page = Pages[i];
                    if (page.OcrResults == null || page.OcrResults.Count == 0)
                    {
                        continue;
                    }

                    ProgressValue = i;

                    // Nếu trang đã dịch thành công trước đó, bỏ qua không dịch lại
                    if (page.IsTranslated)
                    {
                        continue;
                    }

                    StatusText = $"Đang dịch trang {i + 1}/{Pages.Count}: {page.PageName}...";

                    try
                    {
                        var originalTexts = page.OcrResults.Select(r => r.Text).ToList();
                        var translatedTexts = await _translationService.TranslateBatchAsync(originalTexts);

                        for (int j = 0; j < Math.Min(page.OcrResults.Count, translatedTexts.Count); j++)
                        {
                            page.OcrResults[j].Text = translatedTexts[j];
                        }

                        page.IsTranslated = true; // Đánh dấu dịch thành công

                        if (SelectedPage == page)
                        {
                            UpdateOcrResults();
                            OnPropertyChanged(nameof(SelectedPage));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to translate page {page.PageName}: {ex.Message}");
                        failedPages.Add(page.PageName);
                    }
                }

                ProgressValue = Pages.Count;

                if (failedPages.Count > 0)
                {
                    StatusText = $"Dịch hoàn tất. Lỗi ở {failedPages.Count} trang.";
                    ShowMessage?.Invoke($"Đã hoàn thành dịch thuật.\n\nCó {failedPages.Count} trang gặp lỗi không thể dịch:\n" + string.Join("\n", failedPages), "Kết quả dịch", true);
                }
                else
                {
                    StatusText = "Dịch thuật hoàn thành cho tất cả các trang.";
                    ShowMessage?.Invoke("Đã hoàn thành dịch thuật cho tất cả các trang.", "Hoàn thành Dịch thuật", false);
                }
            }
            catch (Exception ex)
            {
                ShowMessage?.Invoke($"Lỗi không xác định khi dịch: {ex.Message}", "Lỗi dịch thuật", true);
                StatusText = "Lỗi dịch thuật.";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// Thực hiện inpaint (xóa chữ) và kích hoạt chế độ hiển thị chữ dịch đè lên ảnh.
        /// </summary>
        private async Task ReplaceAsync()
        {
            if (SelectedPage == null) return;
            var page = SelectedPage;

            if (page.OcrResults == null || page.OcrResults.Count == 0)
            {
                ShowMessage?.Invoke("Trang này chưa được quét OCR. Vui lòng quét OCR và dịch trước khi thay thế.", "Chưa quét OCR", false);
                return;
            }

            // Nếu đã thay thế rồi, bấm lại sẽ tắt chế độ thay thế
            if (page.IsReplaced)
            {
                page.IsReplaced = false;
                OnPropertyChanged(nameof(SelectedPage)); // Báo cho View nạp lại ảnh gốc
                StatusText = "Đã tắt chế độ thay thế.";
                return;
            }

            try
            {
                IsProcessing = true;
                IsProgressIndeterminate = true;
                StatusText = "Đang thực hiện xóa chữ và thay thế bằng chữ dịch...";

                // Thu thập các bounding box
                var boxes = new List<List<List<double>>>();
                foreach (var result in page.OcrResults)
                {
                    boxes.Add(result.Box);
                }

                // Gọi API inpaint
                byte[] cleanImageBytes = await _ocrService.InpaintAsync(page.ImagePath, boxes);

                // Lưu file sạch chữ tạm thời
                string dir = Path.GetDirectoryName(page.ImagePath) ?? "";
                string fileName = Path.GetFileNameWithoutExtension(page.ImagePath) + "_clean.png";
                string cleanPath = Path.Combine(dir, fileName);

                await File.WriteAllBytesAsync(cleanPath, cleanImageBytes);

                page.CleanImagePath = cleanPath;
                page.IsReplaced = true;

                // Nạp hiển thị mới
                OnPropertyChanged(nameof(SelectedPage));
                StatusText = "Thay thế chữ hoàn tất. Mẹo: Cuộn chuột trên chữ dịch để phóng to/thu nhỏ.";
            }
            catch (Exception ex)
            {
                ShowMessage?.Invoke($"Lỗi khi thực hiện thay thế chữ: {ex.Message}", "Lỗi thay thế", true);
                StatusText = "Lỗi thay thế chữ.";
            }
            finally
            {
                IsProcessing = false;
                IsProgressIndeterminate = false;
            }
        }

        /// <summary>
        /// Xuất ảnh đã dịch đè lên vị trí mới.
        /// </summary>
        private async Task ExportAsync()
        {
            var replacedPages = Pages.Where(p => p.IsReplaced && !string.IsNullOrEmpty(p.CleanImagePath) && File.Exists(p.CleanImagePath)).ToList();

            if (replacedPages.Count == 0)
            {
                ShowMessage?.Invoke("Không tìm thấy trang nào đã được thực hiện 'Thay thế' để xuất. Vui lòng bấm nút 'Thay thế' ở các trang bạn muốn xuất trước.", "Thông báo", false);
                return;
            }

            bool? confirm = ShowConfirmDialog?.Invoke(
                $"Bạn có chắc chắn muốn xuất toàn bộ {replacedPages.Count} trang ảnh đã được thay thế chữ dịch không?",
                "Xác nhận xuất ảnh",
                "Export");

            if (confirm != true) return;
            if (RequestFolderSelection == null || SaveImageDelegate == null) return;

            string? outputDir = RequestFolderSelection("Chọn thư mục xuất ảnh đã dịch");
            if (string.IsNullOrEmpty(outputDir)) return;

            try
            {
                IsProcessing = true;
                IsProgressIndeterminate = false;
                ProgressMaximum = replacedPages.Count;
                ProgressValue = 0;

                for (int i = 0; i < replacedPages.Count; i++)
                {
                    var page = replacedPages[i];
                    StatusText = $"Đang xuất ảnh {i + 1}/{replacedPages.Count}: {page.PageName}...";
                    ProgressValue = i;

                    string outputFileName = Path.ChangeExtension(page.PageName, ".png");
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    // Gọi delegate thực thi vẽ giao diện và lưu ảnh từ View
                    await SaveImageDelegate(page, outputPath);
                }

                ProgressValue = replacedPages.Count;
                StatusText = $"Đã xuất thành công {replacedPages.Count} ảnh vào thư mục: {outputDir}";
                ShowMessage?.Invoke($"Đã xuất thành công {replacedPages.Count} ảnh đã được dịch!", "Xuất ảnh hoàn tất", false);
            }
            catch (Exception ex)
            {
                ShowMessage?.Invoke($"Lỗi trong quá trình xuất ảnh: {ex.Message}", "Lỗi xuất ảnh", true);
                StatusText = "Lỗi xuất ảnh.";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        #region Các hàm chức năng phục vụ lệnh menu chuột phải của trang

        private async Task ForceOcrAsync()
        {
            if (SelectedPage == null) return;
            var page = SelectedPage;

            try
            {
                IsProcessing = true;
                StatusText = $"Đang nhận diện lại chữ (Force OCR) cho: {page.PageName}...";
                string lang = SelectedSourceLanguage;
                
                var results = await _ocrService.RecognizeAsync(page.ImagePath, lang);
                page.OcrResults = results;
                page.IsTranslated = false;
                page.IsReplaced = false;
                page.CleanImagePath = null;

                UpdateOcrResults();
                OnPropertyChanged(nameof(SelectedPage));

                StatusText = $"Đã quét lại thành công trang: {page.PageName}";
                ShowMessage?.Invoke($"Đã quét lại thành công trang: {page.PageName}", "Force OCR thành công", false);
            }
            catch (Exception ex)
            {
                ShowMessage?.Invoke($"Lỗi khi quét lại trang {page.PageName}: {ex.Message}", "Lỗi quét lại", true);
                StatusText = "Lỗi quét lại trang.";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ForceTranslateAsync()
        {
            if (SelectedPage == null) return;
            var page = SelectedPage;

            if (page.OcrResults == null || page.OcrResults.Count == 0)
            {
                ShowMessage?.Invoke("Trang này chưa được quét OCR. Vui lòng quét OCR trước.", "Thông báo", false);
                return;
            }

            if (!_translationService.IsKeyConfigured())
            {
                ShowMessage?.Invoke("Vui lòng cấu hình Gemini API Key trong file config.json.", "Chưa cấu hình API Key", true);
                return;
            }

            try
            {
                IsProcessing = true;
                StatusText = $"Đang dịch lại (Force Translate) cho: {page.PageName}...";
                var originalTexts = page.OcrResults.Select(r => r.Text).ToList();
                var translatedTexts = await _translationService.TranslateBatchAsync(originalTexts);

                for (int i = 0; i < Math.Min(page.OcrResults.Count, translatedTexts.Count); i++)
                {
                    page.OcrResults[i].Text = translatedTexts[i];
                }

                page.IsTranslated = true;

                UpdateOcrResults();
                OnPropertyChanged(nameof(SelectedPage));

                StatusText = $"Đã dịch lại thành công trang: {page.PageName}";
                ShowMessage?.Invoke($"Đã dịch lại thành công trang: {page.PageName}", "Force Translate thành công", false);
            }
            catch (Exception ex)
            {
                ShowMessage?.Invoke($"Lỗi khi dịch lại trang {page.PageName}: {ex.Message}", "Lỗi dịch lại", true);
                StatusText = "Lỗi dịch lại trang.";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void ClearResults()
        {
            if (SelectedPage == null) return;
            var page = SelectedPage;

            page.OcrResults = new List<OcrResult>();
            page.IsTranslated = false;
            page.IsReplaced = false;
            page.CleanImagePath = null;

            UpdateOcrResults();
            OnPropertyChanged(nameof(SelectedPage));
            
            StatusText = $"Đã xóa kết quả của trang: {page.PageName}";
        }

        private void DeletePage()
        {
            if (SelectedPage == null) return;
            var page = SelectedPage;

            bool? confirm = ShowConfirmDialog?.Invoke(
                $"Bạn có chắc chắn muốn xóa trang '{page.PageName}' khỏi danh sách không?",
                "Xóa trang",
                "Delete");

            if (confirm != true) return;

            int selectedIndex = Pages.IndexOf(page);
            Pages.Remove(page);

            UpdatePageNumbers();

            if (Pages.Count > 0)
            {
                int nextSelectIndex = Math.Max(0, selectedIndex - 1);
                if (nextSelectIndex < Pages.Count)
                {
                    SelectedPage = Pages[nextSelectIndex];
                }
            }
            else
            {
                SelectedPage = null;
            }

            StatusText = $"Đã xóa trang {page.PageName}.";
        }

        private void DeleteOcr(OcrResult? ocrResult)
        {
            if (ocrResult == null || SelectedPage == null) return;

            bool? confirm = ShowConfirmDialog?.Invoke(
                "Bạn có chắc chắn muốn xóa kết quả OCR này không?",
                "Xác nhận xóa",
                "DeleteOcr");

            if (confirm != true) return;

            SelectedPage.OcrResults.Remove(ocrResult);
            UpdateOcrResults();
            OnPropertyChanged(nameof(SelectedPage));

            StatusText = "Đã xóa kết quả OCR.";
        }

        #endregion

        #region Các helper nội bộ
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

        public void UpdatePageNumbers()
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].PageNumber = i + 1;
            }
        }
        #endregion
    }
}
