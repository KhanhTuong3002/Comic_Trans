using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ComicTrans.Models
{
    public class OcrResult : INotifyPropertyChanged
    {
        private string _text = "";

        [JsonPropertyName("text")]
        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("box")]
        public List<List<double>> Box { get; set; } = new();

        private double _fontSize = 0; // 0 nghĩa là sử dụng tự động co giãn kích thước

        [JsonIgnore]
        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
