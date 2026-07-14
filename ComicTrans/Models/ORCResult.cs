using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ComicTrans.Models
{
    public class OcrResult
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("box")]
        public List<List<double>> Box { get; set; } = new();
    }
}
