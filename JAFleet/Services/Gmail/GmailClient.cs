using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JAFleet.Infrastructure;

namespace JAFleet.Services.Gmail
{
    /// <summary>
    /// Gmail APIの最小クライアント。使うのは「検索」「1通取得」「添付取得」の3つだけなので、
    /// 公式SDK（Google.Apis.Gmail）は入れずHttpClientで直接叩いている。
    ///
    /// 認証は事前に1回だけ同意を通して取っておいたrefresh tokenを使う（Googleの同意画面は
    /// サーバ側で出せないため、取得だけは手元で1回行う）。
    /// client_id / client_secret / refresh_token は環境変数から読む。本番はpassから渡す。
    ///
    /// スコープはgmail.readonlyだけ。ここからメールボックスを書き換えることはできない。
    /// 取込済みかどうかはJAFleet側（jcab_import_session.gmail_message_id）で管理する。
    /// </summary>
    public static partial class GmailClient
    {
        private const string TOKEN_URL = "https://oauth2.googleapis.com/token";
        private const string AUTH_URL = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string API_BASE = "https://gmail.googleapis.com/gmail/v1/users/me";

        /// <summary>読むだけ。これ以上の権限は要らない。</summary>
        public const string SCOPE = "https://www.googleapis.com/auth/gmail.readonly";

        /// <summary>アクセストークンは有効期限の少し手前で取り直す</summary>
        private static readonly TimeSpan EXPIRY_MARGIN = TimeSpan.FromMinutes(2);

        private static readonly SemaphoreSlim _tokenLock = new(1, 1);
        private static string? _accessToken;
        private static DateTime _accessTokenExpiresAt;

        /// <summary>
        /// 送信日時は日本時間で見る。パスワードの「送信年月日」は送信側（霞が関）の日付のため。
        /// IANA名は.NET 6以降のWindowsでも通るが、環境次第なのでWindows名も見る。
        /// </summary>
        private static readonly TimeZoneInfo JST = ResolveJst();

        private static string? ClientId => Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");

        private static string? ClientSecret => Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        private static string? RefreshToken => Environment.GetEnvironmentVariable("GMAIL_REFRESH_TOKEN");

        /// <summary>3つとも揃っていればGmail連携が使える</summary>
        public static bool IsConfigured => HasClient && !string.IsNullOrWhiteSpace(RefreshToken);

        /// <summary>OAuthクライアントだけ揃っている状態。refresh tokenを取りに行くのに必要な分。</summary>
        public static bool HasClient => !string.IsNullOrWhiteSpace(ClientId)
                                     && !string.IsNullOrWhiteSpace(ClientSecret);

        /// <summary>
        /// Googleの同意画面のURL。access_type=offlineとprompt=consentが無いとrefresh tokenが返らない。
        /// redirectUriはGoogle Cloud Consoleに登録したものと完全に一致させる必要がある。
        /// </summary>
        public static string BuildConsentUrl(string redirectUri)
            => $"{AUTH_URL}?client_id={Uri.EscapeDataString(ClientId ?? string.Empty)}"
             + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
             + "&response_type=code"
             + $"&scope={Uri.EscapeDataString(SCOPE)}"
             + "&access_type=offline&prompt=consent";

        /// <summary>
        /// 同意画面から戻ってきた認可コードをrefresh tokenに交換する。
        /// 認可コードは1回しか使えないので、画面を再読込すると失敗する（やり直せばよい）。
        /// </summary>
        public static async Task<string> ExchangeCodeAsync(string code, string redirectUri)
        {
            if (!HasClient)
            {
                throw new InvalidOperationException("GMAIL_CLIENT_ID / GMAIL_CLIENT_SECRET が設定されていません。");
            }

            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["client_id"] = ClientId!,
                ["client_secret"] = ClientSecret!,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            });

            using HttpResponseMessage response = await HttpClientManager.GetInstance().PostAsync(TOKEN_URL, form);
            string content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"認可コードを交換できませんでした（{(int)response.StatusCode}）。{Summarize(content)}");
            }

            using JsonDocument doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("refresh_token", out JsonElement token)
                   && token.GetString() is string refreshToken
                ? refreshToken
                //同じクライアントで前に同意済みだとrefresh tokenが返らないことがある。
                //prompt=consentを付けているので通常は返るが、戻らなければアカウントの
                //「サードパーティのアクセス」から一度解除して取り直す
                : throw new InvalidOperationException(
                    "refresh_tokenが返ってきませんでした。Googleアカウントのアクセス権を一度解除してから取り直してください。");
        }

        /// <summary>Gmailの検索式に当たるメールを新しい順に返す。1通ずつ取りに行くので件数は絞って呼ぶこと。</summary>
        public static async Task<List<GmailMessage>> SearchAsync(string query, int max)
        {
            string listJson = await GetStringAsync(
                $"{API_BASE}/messages?maxResults={max}&q={Uri.EscapeDataString(query)}");

            using JsonDocument list = JsonDocument.Parse(listJson);
            if (!list.RootElement.TryGetProperty("messages", out JsonElement messages))
            {
                //1件も当たらなかったときはmessagesごと返ってこない
                return [];
            }

            List<GmailMessage> result = [];
            foreach (JsonElement message in messages.EnumerateArray())
            {
                string? id = message.GetProperty("id").GetString();
                if (id == null)
                {
                    continue;
                }
                result.Add(await GetMessageAsync(id));
            }
            return result;
        }

        /// <summary>1通の中身（ヘッダ・本文・添付の見出し）を取る。添付の本体は含まれない。</summary>
        public static async Task<GmailMessage> GetMessageAsync(string id)
        {
            string json = await GetStringAsync($"{API_BASE}/messages/{Uri.EscapeDataString(id)}?format=full");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            GmailMessage message = new() { Id = id };

            if (root.TryGetProperty("internalDate", out JsonElement internalDate)
                && long.TryParse(internalDate.GetString(), out long epochMs))
            {
                message.SentAt = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(epochMs), JST);
            }

            if (root.TryGetProperty("payload", out JsonElement payload))
            {
                message.Subject = Header(payload, "Subject");
                message.From = Header(payload, "From");
                message.To = Header(payload, "To");

                StringBuilder body = new();
                CollectParts(payload, message.Attachments, body);
                message.BodyText = body.ToString();
            }

            return message;
        }

        /// <summary>添付の本体を取る</summary>
        public static async Task<byte[]> GetAttachmentAsync(string messageId, string attachmentId)
        {
            string json = await GetStringAsync(
                $"{API_BASE}/messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}");

            using JsonDocument doc = JsonDocument.Parse(json);
            string? data = doc.RootElement.TryGetProperty("data", out JsonElement value) ? value.GetString() : null;
            return data == null
                ? throw new InvalidOperationException("添付の中身が返ってきませんでした。")
                : DecodeBase64Url(data);
        }

        /// <summary>
        /// MIMEの木を辿って、添付（filename付き）と本文（text/plain）を集める。
        /// multipartが入れ子になることがあるので再帰で見る。
        /// </summary>
        private static void CollectParts(JsonElement part, List<GmailAttachment> attachments, StringBuilder body)
        {
            string? fileName = part.TryGetProperty("filename", out JsonElement f) ? f.GetString() : null;
            string? mimeType = part.TryGetProperty("mimeType", out JsonElement m) ? m.GetString() : null;
            JsonElement? partBody = part.TryGetProperty("body", out JsonElement b) ? b : null;

            if (!string.IsNullOrEmpty(fileName))
            {
                GmailAttachment attachment = new()
                {
                    FileName = fileName,
                    MimeType = mimeType,
                    PartId = part.TryGetProperty("partId", out JsonElement partId) ? partId.GetString() : null,
                };
                if (partBody != null)
                {
                    attachment.AttachmentId = partBody.Value.TryGetProperty("attachmentId", out JsonElement aid)
                        ? aid.GetString()
                        : null;
                    attachment.Size = partBody.Value.TryGetProperty("size", out JsonElement size)
                        ? size.GetInt32()
                        : 0;

                    //小さい添付は本体が直接入ってくることがある
                    if (attachment.AttachmentId == null
                        && partBody.Value.TryGetProperty("data", out JsonElement inline)
                        && inline.GetString() is string encoded)
                    {
                        attachment.InlineData = DecodeBase64Url(encoded);
                    }
                }
                attachments.Add(attachment);
            }
            else if (mimeType == "text/plain" && partBody != null
                     && partBody.Value.TryGetProperty("data", out JsonElement text)
                     && text.GetString() is string encodedText)
            {
                body.AppendLine(ResolveEncoding(Header(part, "Content-Type")).GetString(DecodeBase64Url(encodedText)));
            }

            if (part.TryGetProperty("parts", out JsonElement parts))
            {
                foreach (JsonElement child in parts.EnumerateArray())
                {
                    CollectParts(child, attachments, body);
                }
            }
        }

        /// <summary>
        /// 本文のcharset。昔ながらのメールはISO-2022-JPで来ることがあるが、.NET本体は
        /// 日本語のコードページを持っていない（別パッケージが要る）。読めないときはUTF-8として扱い、
        /// 文字化けを承知で先へ進める。本文から拾うのは内線番号だけで、
        /// ASCIIで書かれた問い合わせ先アドレス（register+番号@）からも拾えるため実害は小さい。
        /// </summary>
        private static Encoding ResolveEncoding(string? contentType)
        {
            Match m = CharsetRegex().Match(contentType ?? string.Empty);
            if (!m.Success)
            {
                return Encoding.UTF8;
            }

            try
            {
                return Encoding.GetEncoding(m.Groups[1].Value);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }

        [GeneratedRegex("charset=\"?([^\";]+)\"?", RegexOptions.IgnoreCase)]
        private static partial Regex CharsetRegex();

        private static string? Header(JsonElement payload, string name)
        {
            if (!payload.TryGetProperty("headers", out JsonElement headers))
            {
                return null;
            }

            foreach (JsonElement header in headers.EnumerateArray())
            {
                if (header.TryGetProperty("name", out JsonElement key)
                    && string.Equals(key.GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.TryGetProperty("value", out JsonElement value) ? value.GetString() : null;
                }
            }
            return null;
        }

        private static async Task<string> GetStringAsync(string url)
        {
            string token = await GetAccessTokenAsync();

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using HttpResponseMessage response = await HttpClientManager.GetInstance().SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode
                ? content
                : throw new InvalidOperationException($"Gmail APIが{(int)response.StatusCode}を返しました。{Summarize(content)}");
        }

        /// <summary>
        /// refresh tokenからアクセストークンを取る。期限まではプロセス内で使い回す。
        /// refresh tokenはDBにもディスクにも書かない（環境変数のまま使う）。
        /// </summary>
        private static async Task<string> GetAccessTokenAsync()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "Gmail連携が未設定です。GMAIL_CLIENT_ID / GMAIL_CLIENT_SECRET / GMAIL_REFRESH_TOKEN を設定してください。");
            }

            if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            await _tokenLock.WaitAsync();
            try
            {
                //待っている間に別のリクエストが取り直していることがある
                if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresAt)
                {
                    return _accessToken;
                }

                using FormUrlEncodedContent form = new(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId!,
                    ["client_secret"] = ClientSecret!,
                    ["refresh_token"] = RefreshToken!,
                    ["grant_type"] = "refresh_token",
                });

                using HttpResponseMessage response = await HttpClientManager.GetInstance().PostAsync(TOKEN_URL, form);
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    //invalid_grantはrefresh tokenの失効。取り直しが必要
                    throw new InvalidOperationException(
                        $"アクセストークンを取得できませんでした（{(int)response.StatusCode}）。{Summarize(content)}");
                }

                using JsonDocument doc = JsonDocument.Parse(content);
                _accessToken = doc.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("アクセストークンが空でした。");
                int expiresIn = doc.RootElement.TryGetProperty("expires_in", out JsonElement value)
                    ? value.GetInt32()
                    : 3600;
                _accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn) - EXPIRY_MARGIN;

                return _accessToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>エラー応答はそのまま画面に出すので、長いものは切り詰める</summary>
        private static string Summarize(string content)
            => content.Length <= 300 ? content : content[..300] + "…";

        public static byte[] DecodeBase64Url(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
        }

        private static TimeZoneInfo ResolveJst()
        {
            foreach (string id in new[] { "Asia/Tokyo", "Tokyo Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                    //次の呼び名で探す
                }
            }
            return TimeZoneInfo.Local;
        }
    }
}
