using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComicTrans.Services
{
    public class TranslationService
    {
        private readonly HttpClient _client = new();
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private string? _apiKey;
        private string _modelName = "gemini-flash-lite-latest";

        public TranslationService()
        {
            LoadApiKey();
        }

        private void LoadApiKey()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    var defaultConfig = new 
                    { 
                        GeminiApiKey = "YOUR_GEMINI_API_KEY_HERE",
                        ModelName = "gemini-flash-lite-latest"
                    };
                    File.WriteAllText(_configPath, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
                }

                var json = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("GeminiApiKey", out var apiKeyProp))
                {
                    _apiKey = apiKeyProp.GetString();
                }
                if (doc.RootElement.TryGetProperty("ModelName", out var modelProp))
                {
                    _modelName = modelProp.GetString() ?? "gemini-3.5-flash";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load API key/model: {ex.Message}");
            }
        }

        public bool IsKeyConfigured()
        {
            return !string.IsNullOrEmpty(_apiKey) && _apiKey != "YOUR_GEMINI_API_KEY_HERE";
        }

        public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage = "Vietnamese")
        {
            if (texts == null || texts.Count == 0)
                return new List<string>();

            // Reload key in case they edited config.json while the app was running
            LoadApiKey();

            if (!IsKeyConfigured())
            {
                throw new InvalidOperationException($"Vui lòng cấu hình Gemini API Key hợp lệ trong file config.json tại:\n{_configPath}");
            }

            // Prompt optimized for Manga translation without line breaks
            var systemPrompt = $"You are a professional manga translator translating dialogue from English to {targetLanguage}.\n" +
                               "Below is the sequential list of dialogues/texts extracted from a manga page.\n" +
                               "Please translate each of them naturally, keeping the tone colloquial, expressive, and matching the context of manga dialogue.\n" +
                               "Use appropriate Vietnamese pronouns (cậu, tớ, tôi, anh, em, nó, hắn, ...) based on the comic's context.\n" +
                               "CRITICAL: Do NOT include any line breaks (newlines) in the translated text. The translation for each item must be a single, continuous line without any line breaks, so that the layout engine can wrap it dynamically.\n" +
                               "You MUST output the results ONLY as a JSON array of strings, where each string is the translated text of the corresponding index.";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"{systemPrompt}\n\nInput dialogues:\n{JsonSerializer.Serialize(texts)}" }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "STRING"
                        }
                    }
                }
            };

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            
            HttpResponseMessage? response = null;
            int maxRetries = 3;
            int delayMs = 2000;

            for (int i = 0; i < maxRetries; i++)
            {
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                response = await _client.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                    break;
                
                if ((int)response.StatusCode == 503 || (int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (i < maxRetries - 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"Translation API returned {(int)response.StatusCode}. Retrying in {delayMs * (i + 1)}ms...");
                        await Task.Delay(delayMs * (i + 1));
                        continue;
                    }
                }
                
                response.EnsureSuccessStatusCode();
            }

            if (response == null)
            {
                throw new Exception("Không nhận được phản hồi từ dịch vụ dịch thuật.");
            }

            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            
            var responseText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrEmpty(responseText))
                throw new Exception("Gemini API returned an empty response.");

            List<string>? translatedList = null;
            try
            {
                translatedList = JsonSerializer.Deserialize<List<string>>(responseText);
            }
            catch (Exception ex)
            {
                throw new Exception($"Lỗi phân tích cú pháp bản dịch từ AI. Phản hồi gốc từ AI: {responseText}\nChi tiết lỗi: {ex.Message}");
            }

            if (translatedList == null || translatedList.Count != texts.Count)
            {
                System.Diagnostics.Debug.WriteLine("Warning: Translation count mismatch. Using returned parts.");
                translatedList ??= new List<string>();
            }

            for (int i = 0; i < translatedList.Count; i++)
            {
                string text = EnsureSentenceCase(translatedList[i] ?? "");
                // Thay thế toàn bộ ký tự xuống dòng bằng khoảng trắng
                string cleanText = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                // Loại bỏ khoảng trắng thừa
                while (cleanText.Contains("  "))
                {
                    cleanText = cleanText.Replace("  ", " ");
                }
                translatedList[i] = cleanText.Trim();
            }

            return translatedList;
        }

        private string EnsureSentenceCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            bool hasUppercase = false;
            bool hasLowercase = false;
            foreach (char c in input)
            {
                if (char.IsUpper(c)) hasUppercase = true;
                else if (char.IsLower(c)) hasLowercase = true;
            }

            // Nếu chuỗi chỉ có chữ IN HOA (và không chứa chữ thường nào)
            if (hasUppercase && !hasLowercase)
            {
                var lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length > 0)
                    {
                        // Viết hoa chữ cái đầu dòng, các chữ còn lại viết thường
                        lines[i] = char.ToUpper(line[0]) + line.Substring(1).ToLower();
                    }
                }
                return string.Join(Environment.NewLine, lines);
            }

            return input;
        }
    }
}
