namespace JAFleet.Adsb
{
    /// <summary>軌跡から切り出した1本のフライト（離陸から着陸まで。受信の切れ目で途中から・途中までのこともある）</summary>
    public record FlightLeg(
        string? Callsign,
        TracePoint Start,
        TracePoint End,
        /// <summary>最初に空中にいた点の時刻（離陸の目安）</summary>
        DateTimeOffset AirborneFrom,
        /// <summary>最後に空中にいた点の時刻</summary>
        DateTimeOffset AirborneTo,
        /// <summary>着陸まで追えたか（最後が地上、または低い高度で受信が切れた）</summary>
        bool Landed);

    /// <summary>
    /// 軌跡をフライトに分ける。
    /// 区切りは readsb が付ける「新しい区間」のフラグと、低い高度のまま長く途切れた所（着陸して離陸したとみる）。
    /// 地上の点しか無い区間（駐機・地上走行だけ）はフライトにしない。
    /// </summary>
    public static class FlightLegSplitter
    {
        /// <summary>この高度（ft）以下で受信が切れたら着陸とみる。地方空港は接地まで受信できないことが多い</summary>
        public const int LANDED_MAX_ALTITUDE = 2500;

        /// <summary>途切れの前後がこの高度以下なら、途切れの間に着陸・離陸したとみなして区切る</summary>
        public const int GAP_SPLIT_MAX_ALTITUDE = 10000;

        /// <summary>この長さ以上の途切れを区切りの候補にする</summary>
        public static readonly TimeSpan GAP_SPLIT = TimeSpan.FromMinutes(30);

        /// <summary>空中の点がこれより少ない区間は、誤受信とみて捨てる</summary>
        public const int MIN_AIRBORNE_POINTS = 3;

        public static List<FlightLeg> Split(IReadOnlyList<TracePoint> points)
        {
            var legs = new List<FlightLeg>();
            var current = new List<TracePoint>();
            TracePoint? prev = null;
            foreach (var p in points.OrderBy(p => p.Time))
            {
                if (prev != null && (p.IsNewLeg || IsLandingGap(prev, p) || IsGroundToAir(current, p) || IsCallsignChange(prev, p)))
                {
                    AddLeg(legs, current);
                    current = [];
                }
                current.Add(p);
                prev = p;
            }
            AddLeg(legs, current);
            return legs;
        }

        /// <summary>
        /// callsign が別の便に変わった。受信の切れ目（巡航中の途切れ）をまたいで次の便が始まったときに、
        /// 2便がつながってしまうのを防ぐ。地上走行中に変わったときは、変わる前の地上の点だけの区間ができて捨てられる
        /// </summary>
        private static bool IsCallsignChange(TracePoint before, TracePoint after)
            => before.Callsign != null && after.Callsign != null && before.Callsign != after.Callsign;

        /// <summary>低い高度のまま長く途切れた（その間に着陸して離陸した）</summary>
        private static bool IsLandingGap(TracePoint before, TracePoint after)
            => after.Time - before.Time >= GAP_SPLIT
               && IsLow(before) && IsLow(after);

        /// <summary>着陸して地上にいたあと、また空中に出た（フラグが付かなかったときの保険）</summary>
        private static bool IsGroundToAir(List<TracePoint> current, TracePoint p)
        {
            if (p.OnGround || current.Count == 0 || !current[^1].OnGround)
            {
                return false;
            }
            //この区間ですでに空中にいたことがある＝着陸済み
            return current.Any(c => !c.OnGround);
        }

        private static bool IsLow(TracePoint p) => p.OnGround || (p.Altitude is int a && a <= GAP_SPLIT_MAX_ALTITUDE);

        private static void AddLeg(List<FlightLeg> legs, List<TracePoint> points)
        {
            var airborne = points.Where(p => !p.OnGround).ToList();
            if (airborne.Count < MIN_AIRBORNE_POINTS)
            {
                return;
            }
            //callsign は空中で一番多く出たもの（地上走行中は前の便のままのことがある）
            string? callsign = airborne.Where(p => p.Callsign != null)
                .GroupBy(p => p.Callsign!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
            //区間の頭が前の便の地上走行のことがあるので、始点は最初に空中に出る直前の点にする
            int firstAir = points.IndexOf(airborne[0]);
            var start = points[Math.Max(0, firstAir - 1)];
            var end = points[^1];
            bool landed = end.OnGround || (end.Altitude is int alt && alt <= LANDED_MAX_ALTITUDE);
            legs.Add(new FlightLeg(callsign, start, end, airborne[0].Time, airborne[^1].Time, landed));
        }
    }
}
