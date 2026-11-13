using System.Globalization;
using System.Xml.Linq;
using JAFleet.Infrastructure;

namespace JAFleet.Services.Livery
{
    /// <summary>RSSの1記事分。判定に渡すのはこの4つだけ</summary>
    public record RssItem(string Url, string Title, DateTime? PublishedAt, string? Summary);

    /// <summary>
    /// Aviation WireのRSSを取得して記事の一覧にする。
    /// descriptionは冒頭だけの抜粋なので、本文はGeminiにurl_contextで読ませる。
    /// </summary>
    public static class AviationWireRss
    {
        public static async Task<IReadOnlyList<RssItem>> FetchAsync()
        {
            using HttpRequestMessage request = BuildRequest();
            using HttpResponseMessage response = await HttpClientManager.GetInstance().SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                //403ならUser-Agentを落としていることがほぼ疑わしいので、そこへ誘導する
                throw new HttpRequestException(
                    $"RSSが{(int)response.StatusCode} {response.ReasonPhrase}を返しました。"
                    + "（User-Agentを送らないと403になるサイトです）");
            }

            return Parse(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// RSS取得のリクエスト。User-Agentを必ず付ける
        /// （<see cref="LiveryNewsConstant.USER_AGENT"/> のとおり、空だと403で返ってくる）。
        /// </summary>
        public static HttpRequestMessage BuildRequest()
        {
            HttpRequestMessage request = new(HttpMethod.Get, LiveryNewsConstant.RSS_URL);
            request.Headers.UserAgent.ParseAdd(LiveryNewsConstant.USER_AGENT);
            request.Headers.Accept.ParseAdd("application/rss+xml, application/xml;q=0.9, */*;q=0.8");
            return request;
        }

        /// <summary>RSS2.0のXMLを記事の一覧にする。公開日時の新しい順で返す。</summary>
        public static IReadOnlyList<RssItem> Parse(string xml)
        {
            XDocument doc = XDocument.Parse(xml);

            List<RssItem> items = [];
            foreach (XElement item in doc.Descendants("item"))
            {
                string? url = item.Element("link")?.Value.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                items.Add(new RssItem(
                    url,
                    (item.Element("title")?.Value ?? string.Empty).Trim(),
                    ParseDate(item.Element("pubDate")?.Value),
                    Shorten(item.Element("description")?.Value)));
            }

            return [.. items.OrderByDescending(i => i.PublishedAt ?? DateTime.MinValue)];
        }

        /// <summary>pubDateはRFC1123（例: Thu, 12 Nov 2026 22:37:46 +0000）。日本時間に直して返す</summary>
        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
                ? parsed.ToLocalTime().DateTime
                : null;
        }

        /// <summary>
        /// 抜粋の末尾に付く「続きあり」の印を落として整える。
        /// WordPressはCDATAの中に実体参照を書くため、XMLとして読んだあとも
        /// 「[&amp;#8230;]」という文字列のまま出てくる（「[…]」で来ることもある）。
        /// </summary>
        private static string? Shorten(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            string text = description
                .Replace("[&#8230;]", string.Empty)
                .Replace("[…]", string.Empty)
                .Replace(" ", " ")
                .Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
