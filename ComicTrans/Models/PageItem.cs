using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ComicTrans.Models
{
    public class PageItem : INotifyPropertyChanged
    {
        private string _imagePath = "";
        private string _pageName = "";
        private int _pageNumber;
        private ImageSource? _thumbnail;
        private List<OcrResult> _ocrResults = new();

        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(); }
        }

        public string PageName
        {
            get => _pageName;
            set { _pageName = value; OnPropertyChanged(); }
        }

        public int PageNumber
        {
            get => _pageNumber;
            set { _pageNumber = value; OnPropertyChanged(); }
        }

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        private bool _isTranslated;

        public bool IsTranslated
        {
            get => _isTranslated;
            set { _isTranslated = value; OnPropertyChanged(); }
        }

        public List<OcrResult> OcrResults
        {
            get => _ocrResults;
            set { _ocrResults = value; OnPropertyChanged(); }
        }

        private string? _cleanImagePath;
        public string? CleanImagePath
        {
            get => _cleanImagePath;
            set { _cleanImagePath = value; OnPropertyChanged(); }
        }

        private bool _isReplaced;
        public bool IsReplaced
        {
            get => _isReplaced;
            set { _isReplaced = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
