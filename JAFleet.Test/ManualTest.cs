namespace JAFleet.Test
{
    /// <summary>
    /// 手で動かして様子を見るためのテスト（本物のジョブを動かす・Slack へ投稿する・長く待つ）の目印と入口。
    /// こうしたテストには [TestCategory(ManualTest.CATEGORY)] を付け、先頭で <see cref="RequireOptIn"/> を呼ぶ。
    /// 普段の dotnet test では Inconclusive（スキップ）になり、数十秒で終わる。
    /// 動かすときは環境変数 JAFLEET_RUN_MANUAL_TESTS=1 を付けて、そのテストだけを --filter で指定する。
    /// </summary>
    internal static class ManualTest
    {
        public const string CATEGORY = "Manual";
        public const string OPT_IN_VARIABLE = "JAFLEET_RUN_MANUAL_TESTS";

        public static void RequireOptIn()
        {
            if (Environment.GetEnvironmentVariable(OPT_IN_VARIABLE) != "1")
            {
                Assert.Inconclusive($"手動テストのためスキップ（動かすときは {OPT_IN_VARIABLE}=1）");
            }
        }
    }
}
