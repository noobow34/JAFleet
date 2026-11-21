using AngleSharp.Dom;
using System.Net;

namespace JAFleet.Batch
{
    /// <summary>
    /// Flightradar24 に弾かれていないかの確認。
    /// FR24 は Cloudflare のボット対策でサーバーからのリクエストに 403 を返すことがあり、
    /// そのまま処理すると全機体を「運航なし」「写真なし」で上書きしてしまう。
    /// そこで各バッチは最初の1機だけ先に見て、403 なら何も書き換えずに中止する。
    /// </summary>
    public static class Fr24AccessCheck
    {
        public static bool IsForbidden(IDocument? doc) => doc?.StatusCode == HttpStatusCode.Forbidden;

        public static string BuildMessage(string batchName, string registrationNumber, DateTime now) =>
            $"{batchName}中止:{now}\n" +
            $"最初の1機（{registrationNumber}）でFlightradar24から403（Forbidden）が返ったため、処理を中止しました。\n" +
            "Cloudflareのボット対策でブロックされている可能性があります。DBは書き換えていません。";
    }
}
