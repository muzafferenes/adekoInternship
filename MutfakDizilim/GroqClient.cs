using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace MutfakDizilim
{
    public class GroqClient : IDisposable
    {
        private readonly string apiKey;
        private readonly HttpClient httpClient;
        private readonly int maxRetryCount = 3;

        public GroqClient()
        {
            // config.txt dosyasından API key'i oku
            apiKey = ReadApiKeyFromFile();

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "Groq API key bulunamadı! " +
                    "Proje klasörüne 'config.txt' dosyası oluşturun ve içine Groq API key'inizi yazın."
                );
            }

            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private string ReadApiKeyFromFile()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

                if (File.Exists(configPath))
                {
                    string content = File.ReadAllText(configPath).Trim();
                    return content;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config dosyası okuma hatası: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetResponseFromGroq(string userPrompt)
        {
            for (int attempt = 1; attempt <= maxRetryCount; attempt++)
            {
                try
                {
                    var requestBody = new
                    {
                        messages = new[]
                        {
                            new
                            {
                                role = "system",
                                content = "You are an expert C# programmer. Write only the requested code without explanations. Be precise and concise."
                            },
                            new
                            {
                                role = "user",
                                content = userPrompt
                            }
                        },
                        model = "llama3-70b-8192", // Çok güçlü model
                        max_tokens = 1024,
                        temperature = 0.1, // Kod için düşük temperature
                        top_p = 1,
                        stream = false
                    };

                    string jsonContent = JsonConvert.SerializeObject(requestBody);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await httpClient.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);

                    if (response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();

                        var jsonResponse = JObject.Parse(responseContent);

                        // Hata kontrolü
                        if (jsonResponse["error"] != null)
                        {
                            string errorMessage = jsonResponse["error"]["message"]?.ToString() ?? "Bilinmeyen hata";
                            string errorType = jsonResponse["error"]["type"]?.ToString() ?? "";

                            if (errorType.Contains("quota") || errorType.Contains("rate_limit") || errorMessage.Contains("rate"))
                            {
                                return "❌ QUOTA_ERROR: Groq API rate limit aşıldı";
                            }

                            throw new Exception($"Groq API Hatası: {errorMessage}");
                        }

                        string result = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();

                        if (!string.IsNullOrEmpty(result))
                        {
                            return result.Trim();
                        }
                        else
                        {
                            throw new Exception("Groq'tan boş yanıt alındı");
                        }
                    }
                    else if ((int)response.StatusCode == 429) // Rate limit
                    {
                        return "❌ QUOTA_ERROR: Groq API rate limit aşıldı";
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return "❌ AUTH_ERROR: Groq API key geçersiz";
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"HTTP Hatası {response.StatusCode}: {errorContent}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetryCount)
                    {
                        return $"❌ Bağlantı Hatası: {ex.Message}";
                    }
                    await Task.Delay(1000 * attempt); // Daha kısa bekleme
                }
                catch (TaskCanceledException ex)
                {
                    return "❌ Timeout: Groq API yanıt vermedi";
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("QUOTA_ERROR") || ex.Message.Contains("AUTH_ERROR"))
                    {
                        return ex.Message;
                    }

                    if (attempt == maxRetryCount)
                    {
                        return $"❌ Groq Hatası (Deneme {attempt}/{maxRetryCount}): {ex.Message}";
                    }

                    await Task.Delay(1000 * attempt);
                }
            }

            return "❌ Groq maksimum deneme sayısına ulaşıldı";
        }

        public string GetApiKeyStatus()
        {
            if (string.IsNullOrEmpty(apiKey))
                return "❌ Groq API Key boş";

            if (apiKey.Length < 20)
                return "⚠️ Groq API Key çok kısa (geçersiz olabilir)";

            return $"✅ Groq API Key mevcut ({apiKey.Substring(0, 10)}...{apiKey.Substring(apiKey.Length - 4)})";
        }

        public async Task<string> TestQuota()
        {
            try
            {
                string result = await GetResponseFromGroq("Write a simple C# method that returns 'Hello'. Just the method code.");

                if (result.Contains("QUOTA_ERROR"))
                {
                    return result;
                }
                else if (result.Contains("AUTH_ERROR"))
                {
                    return result;
                }
                else if (result.StartsWith("❌"))
                {
                    return result;
                }

                return "✅ Groq API çalışıyor: " + result.Substring(0, Math.Min(50, result.Length)) + "...";
            }
            catch (Exception ex)
            {
                return $"❌ Groq Test Hatası: {ex.Message}";
            }
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}