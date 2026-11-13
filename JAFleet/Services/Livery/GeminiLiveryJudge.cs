using System.Text;
using System.Text.Json;
using JAFleet.Infrastructure;

namespace JAFleet.Services.Livery
{
    /// <summary>
    /// 記事が「特別塗装機が登場した」ニュースかどうかをGeminiに判定させ、塗装名とレジを抜き出す。
    /// RSSのdescriptionは冒頭の抜粋しかないため、url_contextで記事そのものを読ませている。
    /// </summary>
    public static class GeminiLiveryJudge
    {
        /// <summary>判定結果。rawはGeminiが返したJSONそのまま（livery_news.gemini_resultに残す）</summary>
        public record JudgeResult(GeminiLiveryResponse Response, string Raw);

        /// <summary>レート制限のときだけ待って入れ直す。それ以外のエラーはnullを返して次の記事へ進む</summary>
        public static async Task<JudgeResult?> JudgeWithRetryAsync(string apiKey, string model, RssItem item)
        {
            for (int attempt = 0; attempt < LiveryNewsConstant.MAX_RETRY; attempt++)
            {
                try
                {
                    return await JudgeAsync(apiKey, model, item);
                }
                catch (GeminiRateLimitException)
                {
                    Console.WriteLine($"[LiveryNews] レート制限 → {LiveryNewsConstant.RATE_LIMIT_RETRY_DELAY / 1000}秒待機 (attempt {attempt + 1})");
                    await Task.Delay(LiveryNewsConstant.RATE_LIMIT_RETRY_DELAY);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LiveryNews] Geminiエラー: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private static async Task<JudgeResult?> JudgeAsync(string apiKey, string model, RssItem item)
        {
            var requestBody = new
            {
                //本文を読ませる。AviationWireは続きが有料の記事もあるが、冒頭に塗装名とレジが載る
                tools = new[] { new { url_context = new { } } },
                contents = new[]
                {
                    new { parts = new object[] { new { text = BuildPrompt(item) } } }
                }
            };

            string url = string.Format(LiveryNewsConstant.GEMINI_API_URL_TEMPLATE, model);
            HttpResponseMessage response = await HttpClientManager.GetInstance().PostAsync(
                $"{url}?key={apiKey}",
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            string responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new GeminiRateLimitException();
                }
                throw new Exception($"Gemini APIエラー ({(int)response.StatusCode}): {responseText}");
            }

            string? raw = ExtractText(responseText);
            if (raw == null)
            {
                Console.WriteLine($"[LiveryNews] Geminiの応答に本文がありません: {item.Url}");
                return null;
            }

            GeminiLiveryResponse? parsed = Deserialize(raw);
            return parsed == null ? null : new JudgeResult(parsed, raw);
        }

        /// <summary>
        /// プロンプト。判定の線引きはここだけで決まるので、直すときはここを見る。
        /// URLが無いとき（お試し判定で本文を貼った場合）は貼られた文章だけで判定させる。
        /// </summary>
        public static string BuildPrompt(RssItem item)
        {
            bool hasUrl = !string.IsNullOrWhiteSpace(item.Url);
            string source = hasUrl
                ? "URLの内容を読んでから判定してください。"
                : "記事の本文は下に貼ってあるものだけです。これだけで判定してください。";
            string urlLine = hasUrl ? $"URL: {item.Url}" : string.Empty;

            //$$にすると{}がそのまま書けるので、返させるJSONの例を素直に置ける（埋め込みは{{ }}）
            return $$"""
                次の記事が「日本の航空会社の機体に特別塗装（特別塗装機・スペシャルマーキング）が新しく登場した」ことを伝える記事かどうかを判定してください。
                {{source}}

                {{urlLine}}
                タイトル: {{(string.IsNullOrWhiteSpace(item.Title) ? "（なし）" : item.Title)}}
                本文・概要: {{item.Summary ?? "（なし）"}}

                【判定ルール】
                - 特別塗装機が新たに登場した（塗装された・公開された・就航した・運航を始める）記事だけをtrueにしてください。
                - すでに飛んでいる特別塗装機が話題や写真として出てくるだけの記事はfalseにしてください。
                - 特別塗装の終了・退役・通常塗装への復帰はfalseにしてください。
                - 日本の航空会社の機体（レジが「JA」で始まる機体）の話でなければfalseにしてください。
                - 機内や客室の特別仕様、ロゴの変更、空港や施設の装飾は特別塗装ではありません。

                【抽出ルール】
                - trueの場合はtargetsに対象機を入れてください。複数機あればその数だけ入れてください。
                - registrationは記事に書かれたレジ（例: JA01XX）をそのまま入れ、書かれていなければnullにしてください。
                - livery_nameは特別塗装の名称だけを短く入れてください（例:「ピカチュウジェット」「〇〇塗装」）。
                  記事の文章をそのまま入れないでください。
                - falseの場合はtargetsを空配列にしてください。
                - reasonには判定の理由を1文で書いてください。
                - JSON形式のみで返答し、他のテキストは一切含めないでください。

                {
                  "special_livery": true または false,
                  "reason": "判定理由を1文で",
                  "targets": [
                    { "registration": "JA01XX", "livery_name": "塗装名" }
                  ]
                }
                """;
        }

        /// <summary>candidates[0]のpartsに入っているテキストをつなげる。url_contextを使うとpartsが複数になることがある</summary>
        private static string? ExtractText(string responseText)
        {
            using JsonDocument doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) || candidates.GetArrayLength() == 0)
            {
                return null;
            }
            if (!candidates[0].TryGetProperty("content", out JsonElement content)
                || !content.TryGetProperty("parts", out JsonElement parts))
            {
                return null;
            }

            StringBuilder sb = new();
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement text))
                {
                    sb.Append(text.GetString());
                }
            }

            string joined = sb.ToString().Trim();
            return joined.Length == 0 ? null : joined;
        }

        /// <summary>JSONだけを返させているが、マークダウンのコードブロックで包まれてくることがあるので外す</summary>
        public static GeminiLiveryResponse? Deserialize(string raw)
        {
            string text = raw.Trim();
            if (text.StartsWith("```"))
            {
                text = string.Join("\n", text.Split('\n').Skip(1));
            }
            if (text.EndsWith("```"))
            {
                text = text[..text.LastIndexOf("```", StringComparison.Ordinal)];
            }
            text = text.Trim();

            try
            {
                GeminiLiveryResponse? result = JsonSerializer.Deserialize<GeminiLiveryResponse>(text,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    });

                //対象機が挙がっているのにfalseで返ってくることがあるので揃える
                if (result != null && result.Targets is { Count: > 0 } && !result.SpecialLivery)
                {
                    Console.WriteLine("[LiveryNews] special_liveryがfalseだがtargetsがあるため補正します");
                    result.SpecialLivery = true;
                }

                return result;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[LiveryNews] Geminiの応答を解釈できません: {ex.Message}");
                return null;
            }
        }

        private sealed class GeminiRateLimitException : Exception { }
    }

    public sealed class GeminiLiveryResponse
    {
        public bool SpecialLivery { get; set; }
        public string? Reason { get; set; }
        public List<GeminiLiveryTarget>? Targets { get; set; }
    }

    public sealed class GeminiLiveryTarget
    {
        public string? Registration { get; set; }
        public string? LiveryName { get; set; }
    }
}
