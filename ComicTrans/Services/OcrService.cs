using ComicTrans.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace ComicTrans.Services
{
    public class OcrService
    {
        private readonly HttpClient _client = new();

        public async Task<List<OcrResult>> RecognizeAsync(string imagePath, string lang = "en")
        {
            using var form = new MultipartFormDataContent();

            using var fs = File.OpenRead(imagePath);

            var fileContent = new StreamContent(fs);

            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue("image/png");

            form.Add(fileContent, "image", Path.GetFileName(imagePath));
            form.Add(new StringContent(lang), "lang");

            var response = await _client.PostAsync(
                "http://127.0.0.1:5000/ocr",
                form);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<List<OcrResult>>(json)!;
        }

        public async Task<List<OcrResult>> RecognizeBytesAsync(byte[] imageBytes, string lang = "en")
        {
            using var form = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(imageBytes);

            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue("image/png");

            form.Add(fileContent, "image", "cropped.png");
            form.Add(new StringContent(lang), "lang");

            var response = await _client.PostAsync(
                "http://127.0.0.1:5000/ocr",
                form);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<List<OcrResult>>(json)!;
        }

        public async Task<byte[]> InpaintAsync(string imagePath, List<List<List<double>>> boxes)
        {
            using var form = new MultipartFormDataContent();

            using var fs = File.OpenRead(imagePath);

            var fileContent = new StreamContent(fs);

            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue("image/png");

            form.Add(fileContent, "image", Path.GetFileName(imagePath));

            var jsonBoxes = JsonSerializer.Serialize(boxes);
            form.Add(new StringContent(jsonBoxes), "boxes");

            var response = await _client.PostAsync(
                "http://127.0.0.1:5000/inpaint",
                form);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}
