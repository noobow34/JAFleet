using System.Collections.Concurrent;
using JAFleet.Batch;
using JAFleet.Services.Livery;

namespace JAFleet.Jobs
{
    /// <summary>
    /// /Scheduler に「実行中」と途中経過を出すための記録。
    /// Quartz から動いたものは Quartz が実行中を知っているが、管理コンソールのボタンから直接動かしたもの
    /// （Task.Run で処理クラスを呼ぶ）は知らないので、処理クラスの Processing フラグも見る。
    /// 途中経過は、長い処理（adsb.lol のダンプの取り込みなど）が最新の1行を入れておく。
    /// </summary>
    public static class JobActivity
    {
        private static readonly ConcurrentDictionary<string, (string Text, DateTime At)> progress = new(StringComparer.Ordinal);

        /// <summary>途中経過の最新の1行を入れる</summary>
        public static void Report(Type jobType, string text)
            => progress[jobType.FullName!] = (text, DateTime.Now);

        /// <summary>終わったら消す</summary>
        public static void Clear(Type jobType) => progress.TryRemove(jobType.FullName!, out _);

        /// <summary>途中経過（無ければ null）</summary>
        public static (string Text, DateTime At)? Progress(string className)
            => progress.TryGetValue(className, out var p) ? p : null;

        /// <summary>ジョブのクラス名に対応する処理クラスが、手動起動も含めて動いているか</summary>
        public static bool IsProcessing(string className)
        {
            if (className == typeof(RefreshAdsbFlightJob).FullName) return RefreshAdsbFlight.Processing;
            if (className == typeof(RefreshAircraftHexJob).FullName) return RefreshAircraftHex.Processing;
            if (className == typeof(RefreshWorkingStatusAndPhotoJob).FullName) return RefreshWorkingStatusAndPhoto.Processing;
            if (className == typeof(RefreshRetiredAircraftPhotoJob).FullName) return RefreshPhoto.Processing;
            if (className == typeof(CheckSpecialLiveryNewsJob).FullName) return LiveryNewsChecker.Processing;
            return false;
        }
    }
}
