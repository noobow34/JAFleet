using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace JAFleet.Infrastructure
{
    /// <summary>
    /// 稼働中のバージョン情報（管理者向け表示用）。
    /// コミットハッシュとコミット日時はビルド時にcsprojのEmbedCommitInfoターゲットがアセンブリへ埋め込む。
    /// DBに持たないので、ロールバックしても表示と実際に動いているバイナリがずれない。
    /// </summary>
    public static partial class AppVersion
    {
        private const string RepoUrl = "https://github.com/noobow34/JAFleet";

        public static string CommitHash { get; } = GetMetadata("CommitHash") ?? "unknown";

        public static string? CommitUrl { get; } =
            CommitHash == "unknown" ? null : $"{RepoUrl}/commit/{CommitHash}";

        public static DateTime? CommitDate { get; } =
            DateTimeOffset.TryParse(GetMetadata("CommitDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.LocalDateTime : null;

        /// <summary>
        /// デプロイ日時。jr.shは releases/yyyyMMdd-HHmmss-{sha} へ発行して current をそこへ向けるので、
        /// 実行ディレクトリ名から取る。その形式でない環境（開発機など）はアセンブリの更新日時で代用する。
        /// </summary>
        public static DateTime DeployDate { get; } = GetDeployDate();

        private static string? GetMetadata(string key) =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value;

        private static DateTime GetDeployDate()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            // current シンボリックリンク経由で起動していても実体のディレクトリ名を見る
            var name = (dir.ResolveLinkTarget(true) as DirectoryInfo ?? dir).Name;
            var m = ReleaseDirRegex().Match(name);
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return d;
            }
            return File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location);
        }

        [GeneratedRegex(@"^(\d{8}-\d{6})-")]
        private static partial Regex ReleaseDirRegex();
    }
}
