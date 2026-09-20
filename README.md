# JA-Fleet

日本の航空会社が保有する機材（飛行機）を一覧表示・検索できるサイト **JA-Fleet** の Web アプリケーションです。

本番サイト: https://ja-fleet.noobow.me/

レジ（登録記号）ごとの機体情報、写真、稼働状況、履歴を公開するとともに、
管理者向けに機体情報の編集、航空局 Excel の取込、稼働状況チェックのバッチ実行などを提供します。

## 主な機能

### 公開機能

| 機能 | パス | 説明 |
| --- | --- | --- |
| トップ | `/` | 航空会社グループ別のリンクとクイックリンク |
| 機体一覧 | `/Aircraft/AirlineGroup/{id}`<br>`/Aircraft/Airline/{id}`<br>`/Aircraft/Type/{id}` | グループ・航空会社・型式ごとの一覧 |
| 機体詳細 | `/AD/{レジ}`（`/AircraftDetail/{レジ}`） | 1 機の詳細と更新履歴。`/ADN`（ヘッダ無し）、`/ADNB`（ヘッダ無し＋戻る）、`/ADE`（写真の埋め込み）の派生あり |
| 検索 | `/Search` | 航空会社・型式・運用状況・Wi-Fi などの条件検索。条件は名前を付けて保存でき、`?sc={キー}` で共有できる |
| 非稼働情報 | `/NotWorkingInfo` | 一定期間飛んでいない機体の一覧 |
| 稼働チェックログ | `/WorkingCheckLog` | 稼働状況が変化した機体の記録 |
| 問い合わせ | 各ページのモーダル（`wwwroot/message.html`）→ `POST /Message/Send` | 送信内容を Slack へ通知し、`message` テーブルへ記録 |
| LINE | `/Line` | 公式アカウントへリダイレクト（アクセスをログに記録） |
| 死活監視 | `/Check` | `aircraft_view` の件数を返す。アクセスログ・自動ログインの対象外 |

### JSON API

`/api` 配下は自動ログインの対象外で、外部から直接叩けます。

| パス | 説明 |
| --- | --- |
| `GET /api/AirlineGroup`, `GET /api/AirlineGroup/{id}/{id2?}` | 航空会社グループの一覧・機体一覧 |
| `GET /api/Airline`, `GET /api/Airline/{id}/{id2?}` | 航空会社の一覧・機体一覧 |
| `GET /api/Type`, `GET /api/Type/{id}` | 型式の一覧・機体一覧 |
| `GET /api/Reg`, `GET /api/Reg/{id}` | レジの一覧・単票 |
| `GET /api/AircraftWithHistory/{id}` | 機体情報＋更新履歴 |

一覧系は `?includeRetire=true` で退役機を含められます。

### 管理機能

管理者向けの機能です。各画面は Cloudflare Access でのログイン（`AdminAuth.IsAdmin`）を前提としています。

| 機能 | パス | 説明 |
| --- | --- | --- |
| 管理コンソール | `/Admin` | 以下の各機能への入口。レジ検索、編集・詳細への直行 |
| 単票編集 | `/E/{レジ}` | 機体情報の新規登録・更新。更新時は `aircraft_history` へ退避 |
| 同一型式の一括登録 | `/BulkRegister` | 「レジ / 登録年月日 / 製造番号」をタブ・カンマ・空白区切りで貼り付けてまとめて登録 |
| 航空局 Excel 取込 | `/JcabImport` | 国土交通省航空局から届くパスワード付き Excel を解除して取り込む |
| マスタ再読込 | `/ReloadMaster` | 航空会社・型式などのマスタをメモリへ読み直す |
| 定期実行ジョブ | `/Scheduler` | Quartz のジョブの有効・無効と cron 式を変更する |
| バッチ手動実行 | `/Batch/RefreshWorkingStatusAndPhoto`<br>`/Batch/RefreshPhoto` | 定期ジョブと同じ処理を手動で起動 |
| アクセスログ | `/log`（本日）、`/logy`（昨日） | 閲覧・検索の記録 |
| アクセス集計 | `/AccessStats` | `access_log` を集計して PV・訪問者・人気ページなどを表示 |

#### 航空局 Excel 取込の流れ

1. `/JcabImport` にファイルをアップロード。パスワードは送信元から通知されている規則に従って組み立て、
   受け取りから日が空いていても開けるよう、送信日を数日分遡って自動で試す。
   組み立てに使う値は `code` テーブルに持たせ、リポジトリには置かない。
2. NEW / DEL / TRAN / CNG / RESERVATION / CANCEL の各シートを解析し、レジ単位のプレビューを作る。
   プレビューは「そのまま取り込める」「人の判断が要る」「移転登録」「対象外」に分類される。
3. 途中で保存すると `jcab_import_session` に復号済みファイルと編集内容（JSON）が残り、後から再開できる。
   再開時は Excel を解析し直したうえで編集内容を被せるため、最新のマスタ・機体情報と突き合わせられる。
4. 実行すると `AircraftStore` 経由で 1 トランザクションにまとめて登録・更新する。

#### アクセス集計

`/AccessStats` は `LoggingMiddleware` が書いている `access_log` を集計します。
これで自前で数えられるようになったため、**Google Analytics は撤去しました**。
サイトは外部のアクセス解析を使っておらず、閲覧の記録はすべて自前の DB に閉じています。
`/log` が「検索した」「詳細を見た」といったアプリ側の出来事（`log` テーブル）を 1 件ずつ並べるのに対し、
こちらは **HTTP リクエストそのもの**を数えます。

- 期間は 1 / 3 / 7 / 14 / 30 日から選べる。`access_log` はサーバの `log_process.sh` が毎晩 31 日より
  古い行を消すため、これ以上は遡れない
- 数えるのは**コントローラーが返したページ**だけ。静的ファイル・JSON API・AJAX は「対象外」として
  件数だけ出す（判定は後述）
- 管理者自身（`is_admin`）のアクセスと、User-Agent に含まれる語（`UserAgentUtil.BOT_KEYWORDS`）で
  判定したボットは、どちらも**既定で除外**。画面のボタンでそれぞれ含める／除くを切り替えられる
- 出すもの: PV・訪問者数・ユニーク IP・平均応答時間・エラー件数・対象外件数、日別と時間帯別の推移、
  人気ページ、機体詳細のレジ別ランキング（`/AD` `/ADN` `/ADNB` `/ADE` `/AircraftDetail` をまとめる）、
  外部からの流入元、コントローラー別・端末・ステータスコードの内訳、遅いページ
- 集計は DB 側の GROUP BY で行い、アプリへは集計済みの行だけを持ってくる

集計項目が多く問い合わせが重いため、画面は**枠と中身に分けて**あります。

| | 返すもの | DB |
| --- | --- | --- |
| `GET /AccessStats` | 見出し・期間の切り替え・読み込み中の表示だけ（`Index.cshtml`） | 触らない |
| `GET /AccessStats/Data` | 集計の中身（部分ビュー `_Stats.cshtml`） | 集計する |

枠が即座に返るので、管理コンソールから開いた直後にページが出てスピナーが回ります。
期間やボットの切り替えは普通のリンクのまま（枠が軽いので読み直しても待たされない）で、
読み直すたびに中身だけを取り直します。

##### ページの判定（route_key）

`LoggingMiddleware` は後続処理を通したあとで MVC のルートを見て、
`access_log.route_key` に `"コントローラー名/アクション名"` を記録します。

- 静的ファイルやどのルートにも当たらなかったリクエストは、ルートが無いので `route_key` が `NULL`。
  拡張子の一覧を持たなくても、集計側はこれだけで弾ける
- JSON API（`[ApiController]` の `AirlineGroup` / `Airline` / `Type` / `Reg` / `AircraftWithHistory`）、
  画面から叩く AJAX（`Master`）、押すだけで画面を返さないもの（`Batch` / `ReloadMaster` / `SetCookie`）、
  死活監視系（`Check` など）はコントローラー名で除外する。一覧は `AccessPath.NON_PAGE_CONTROLLERS`
- 内訳の「コントローラー別」はアクションをまとめた単位。`/AD` と `/ADN` と `/ADE` は
  同じ `AircraftDetail` なので 1 つに数えられる

**`route_key` 列を足す前に記録された行はすべて `NULL`** で、そのままだと過去 31 日分が丸ごと
対象外になります。その間だけパスから推測して拾う**移行措置**を入れてあります。

- ページのパスには拡張子が付かない（`/`、`/AD/JA226P`、`/Search`）のに対し静的ファイルには必ず付くので、
  ピリオドの有無で大半を見分ける。加えて `AccessPath.LEGACY_NON_PAGE_PREFIXES` のパスを除く
- コントローラーの呼び名は `AccessPath.LegacyDisplayName` がパスの先頭から当てる
  （`/AD` `/ADNB` → `AircraftDetail`、`/logy` → `Log` のようにルーティングの別名を吸収する）
- SQL は「`route_key` のある行」と「無い行」を `Concat`（`UNION ALL`）で繋いだものを集計元にしている
- **デプロイから 1 か月たって `access_log` が入れ替わったら、この移行措置は削除してよい**。
  `AccessPath` の「ここから移行措置」の区画、`AccessStatsService.Legacy`、
  `BuildControllersAsync` の legacy の塊が対象

##### 訪問者の識別（Cookie）

IP は携帯回線などで共有されるため、訪問者数は Cookie で数えています。

- `VisitorIdIssuer` が `jaf_vid` Cookie を発行する。中身は **DataProtection で保護した文字列**で、
  暗号化と改ざん検知が付くため、利用者が値を作って集計を水増しすることはできない
- 中身はランダムな 16 桁と Cookie を配った日付だけで、個人を特定する情報は持たない。
  HttpOnly / SameSite=Lax、有効期限 400 日。配ってから 30 日たったら同じ ID で配り直す
- 鍵は DataProtection のキーリングに載り、アプリ名も固定してあるので、デプロイや再起動をまたいでも
  同じ訪問者として数えられる
- `access_log.visitor_id` に入るのは **クライアントが送ってきて検証に通った ID だけ**。
  新しく発行した分は `NULL` のままにしてあるので、Cookie を持ち回らないクライアント（多くのボット）が
  リクエストのたびに別の訪問者として数えられることはない。代わりに各訪問者の最初の 1 リクエストは
  訪問者数に入らない
- `/api`、`/Check`、`/Master`、`/SetCookie` には配らない

## 技術スタック

- .NET 10 / ASP.NET Core MVC（Razor Views）
- PostgreSQL + Entity Framework Core（Npgsql）
- Cloudflare Access による管理者認証（`Microsoft.AspNetCore.Authentication.JwtBearer` で JWT を検証）
- Quartz.NET によるジョブスケジューリング
- AngleSharp によるスクレイピング（Flightradar24 / JetPhotos）
- EPPlus（航空局 Excel の解析。非商用ライセンス）
- AutoMapper、Crc32.NET、BuildBundlerMinifier、WebEssentials.AspNetCore.PWA
- Bootstrap 3 / jQuery / jQuery UI / DataTables
- テストは MSTest

## ソリューション構成

`JAFleet.sln` は次の 4 プロジェクトで構成されます。

| プロジェクト | 位置 | 役割 |
| --- | --- | --- |
| `JAFleet` | `JAFleet/` | Web アプリ本体 |
| `JAFleet.Test` | `JAFleet.Test/` | 単体テスト |
| `JAFleet.Commons` | `../JAFleet.Commons/` | EF エンティティ（`JAFleetContext`）、定数、スクレイピング処理 |
| `Noobow.Commons` | `../Noobow.Commons/` | Slack 通知などの汎用ユーティリティ |

`JAFleet.Commons` と `Noobow.Commons` は別リポジトリです。ビルドするにはこのリポジトリと同じ階層に clone してください。

- https://github.com/noobow34/JAFleet.Commons
- https://github.com/noobow34/Noobow.Commons

```
（親ディレクトリ）
├── JAFleet/          ← このリポジトリ
├── JAFleet.Commons/
└── Noobow.Commons/
```

## ディレクトリ構成

```
JAFleet/
├── Controllers/      画面・API のコントローラー
├── Views/            Razor ビュー
├── Models/           画面用モデル
├── Services/         業務ロジック
│   ├── BulkRegister/ 同一型式一括登録のパーサ
│   ├── JcabImport/   航空局 Excel の解除・解析・プレビュー生成
│   ├── AccessStatsService.cs   access_log の集計（/AccessStats）
│   ├── AircraftStore.cs        機体の登録・更新（単票編集と一括取込の共通処理）
│   ├── SearchConditionStore.cs 名前付き検索条件の登録
│   ├── TypeDetailStore.cs      詳細型式のその場登録
│   └── MasterManager.cs        マスタのメモリキャッシュ
├── Batch/            スクレイピングを伴う長時間処理と Slack 通知の組み立て
├── Jobs/             Quartz のジョブと RootScheduler
├── Middleware/       アクセスログ記録、条件付き自動ログイン
├── Infrastructure/   管理者判定・Cloudflare Access・訪問者 Cookie・ハッシュ・HttpClient のユーティリティ
├── Ddl/              手動適用する DDL
└── wwwroot/          静的ファイル

JAFleet.Test/         MSTest のテスト
```

## 定期実行ジョブ

ジョブは `scheduler_def` テーブルに **クラス名と cron 式** を登録し、起動時に `RootScheduler` が読み込んで
Quartz に登録します（`Enabled` が true の行のみ）。

有効・無効の切り替えと cron 式の変更は `/Scheduler` から行います。保存すると DB を書き換えたうえで
動いているスケジューラへその場で反映するため、再起動は要りません。画面には `scheduler_def` の行だけでなく
アプリが持つ `IJob` の実装がすべて並ぶので、定義が無いジョブもここから有効にできます
（`JobCatalog` がクラスを集め、`[Description]` の文言を説明として出します）。

画面で無効にしても、そのとき実行中の処理は止まりません。Quartz は動いているジョブを中断できないため、
最後まで走り切ってから次回以降が実行されなくなります。

cron 式は Quartz の書式で、Unix の cron と違い **秒から始まる 6〜7 項目**です（例: 毎日 6 時は `0 0 6 * * ?`）。
保存時に `CronExpression.IsValidExpression` で検査し、通らない式は登録しません。

| ジョブ | 内容 |
| --- | --- |
| `RefreshWorkingStatusAndPhotoJob` | 退役以外の全機体について Flightradar24 を巡回し、稼働状況と写真を更新。変化を稼働チェックログと Slack 通知用テキストとして記録 |
| `RefreshRetiredAircraftPhotoJob` | 退役・抹消済み機体の写真のみ更新 |
| `NotifyWorkingStatusJob` | その日の稼働状況の変化を Slack へ投稿。更新処理が実行中なら完了を待ってから投稿し、未実行なら警告する |

## 環境変数

アプリは設定を環境変数から読みます（`appsettings.json` にはログレベル等のみ）。

| 変数 | 用途 |
| --- | --- |
| `JAFLEET_CONNECTION_STRING` | PostgreSQL の接続文字列 |
| `CF_ACCESS_TEAM_DOMAIN` | Cloudflare Zero Trust のチームドメイン（例: `noobow.cloudflareaccess.com`）。JWT の `iss` と公開鍵の取得先になる |
| `CF_ACCESS_AUD` | Access アプリケーションの Application Audience (AUD) タグ。JWT の `aud` と突き合わせる |
| `ADMIN_KEY` / `ADMIN_VALUE` | 管理者の端末を見分けるための Cookie の名前と値。一致した場合のみ未認証時に `/Account/Login` へ自動リダイレクトする（認可そのものは Cloudflare Access が行う） |
| `SLACK_BOT_TOKEN` | Slack 通知（`Noobow.Commons` の `SlackUtil`） |
| `ENCRYPTION_KEY` | `Noobow.Commons` の `AesEncryption` が使用 |
| `CF_ACCESS_DEV_ADMIN` | 開発専用。`ASPNETCORE_ENVIRONMENT=Development` かつ `1` のときだけ、認証済みの管理者になりすます（手元では Access の JWT が手に入らないため） |

## 管理者認証（Cloudflare Access）

サイトはほぼ全ページが認証なしで見え、**管理者だけを見分けて**編集リンクや管理機能を出す、という作りです。
Cloudflare Access はプロキシとして認証しますが、通過したリクエストに JWT を渡してくるので、
これを検証すれば公開ページでも管理者を識別できます。

### 仕組み

1. Access アプリケーションは **`/Account/Login` だけ**を保護する。他のページは Access の外に置く。
2. 管理者が `/Account/Login` を踏むと Cloudflare のログイン画面が出て、通過すると
   ホストに `CF_Authorization` Cookie（中身は JWT）が発行される。
3. この Cookie はパス `/` で発行されるためブラウザが全ページへ送る。
   アプリは `CloudflareAccess`（`Infrastructure/CloudflareAccess.cs`）でこれを検証し、
   成功すれば `User.Identity.IsAuthenticated` が立つ。判定は従来どおり `AdminAuth.IsAdmin` で行う。
4. 公開鍵は `https://{CF_ACCESS_TEAM_DOMAIN}/cdn-cgi/access/certs` から取得して 1 時間キャッシュする。
   Access には OIDC のディスカバリ文書が無いため、`Authority` ではなく `IssuerSigningKeyResolver` で直接引いている。

`ConditionalAuthRedirectMiddleware` は、`ADMIN_KEY`/`ADMIN_VALUE` の Cookie を持つ端末が未認証のときだけ
`/Account/Login` へリダイレクトします。一般の利用者はこの Cookie を持たないのでログイン画面を見ることはありません。
Cookie は `/SetCookie?key=...&value=...` で仕込みます。

`/Account/Logout` は `/cdn-cgi/access/logout` へ送るだけで、目印の Cookie は消しません。
そのため目印を持つ端末はログアウト直後にまたログイン画面へ飛ばされますが、基本はログインしたまま使う前提で、
部外者は Access を通れないので問題ありません。

### Access アプリケーションの設定

| 項目 | 値 |
| --- | --- |
| Application type | Self-hosted |
| Path | `ja-fleet.noobow.me/Account/Login` |
| Session Duration | 任意（切れると次回アクセス時に再ログインになる） |
| Policy | Allow / Emails → 管理者のメールアドレス |
| IdP | One-time PIN で足りる |

発行された **Application Audience (AUD) タグ**を `CF_ACCESS_AUD` に設定します。

管理機能のパス（`/Admin`、`/E`、`/JcabImport` など）は **Access の保護対象に入れていません**。
アプリ側が `AdminAuth.IsAdmin` で `NotFound` を返しており、パスの存在自体を隠せるためです。
Access で保護するとログイン画面が出てしまい、パスが存在することが分かってしまいます。

同一ホストに複数の Access アプリケーションを作ると `CF_Authorization` の `aud` が混ざるため、
アプリケーションは 1 つに保ってください。

### 公開経路（Cloudflare Tunnel）

`cloudflared` が Kestrel へ直接つなぎます。Caddy は廃止しました。

```
Cloudflare Edge → cloudflared → Kestrel(localhost:5000)
```

`~/.cloudflared/config.yml`:

```yaml
tunnel: <TUNNEL-ID>
credentials-file: /home/noobow/.cloudflared/<TUNNEL-ID>.json

ingress:
  - hostname: ja-fleet.noobow.me
    service: http://localhost:5000
  - service: http_status:404
```

TLS はエッジが終端するため、Tunnel へ寄せたあとは 80/443 を閉じられます。

Caddy が担っていた機能の行き先は次のとおりです。

| Caddy の設定 | 移行後 |
| --- | --- |
| 自動 HTTPS | Cloudflare のエッジ証明書 |
| `encode zstd gzip` | Cloudflare が自動で Brotli / gzip をかける |
| `Strict-Transport-Security` ヘッダ | Cloudflare の SSL/TLS → HSTS を有効化する |
| `handle /mainte/*` と `handle_errors 5xx` のメンテナンス画面 | **廃止**。デプロイ中や 5xx 時は Cloudflare の既定エラー画面になる |
| `lb_try_duration` による再起動中のリトライ | 同等機能なし。再起動の数秒間はエラーが露出する |

アプリの IP ロギング（`LoggingMiddleware`）は `X-Forwarded-For` を直接読んでおり、
Cloudflare が実クライアント IP を入れて渡すため、この構成変更の影響を受けません。

> **キャッシュ注意**: 管理者向けの列やリンクを含む HTML がエッジにキャッシュされると一般利用者に出ます。
> Cloudflare は既定で HTML をキャッシュしませんが、Cache Everything 系のルールは入れないでください。
> 入れる場合は `CF_Authorization` Cookie の有無で Bypass する必要があります。

## ビルドと実行

```bash
dotnet build JAFleet.sln
```

```bash
dotnet run --project JAFleet/JAFleet.csproj
```

起動時に `MasterManager.ReadAll` でマスタをメモリに読み込み、`RootScheduler` がジョブを登録するため、
**起動には DB 接続が必要**です。

CSS / JS は `bundleconfig.json` に従いビルド時に `site.min.css` / `site.min.js` へバンドル・最小化されます。

## テスト

```bash
dotnet test JAFleet.sln
```

`WorkingStatusSlackNotifyTest` など一部のテストは Slack へ実際に投稿します。実行時は `SLACK_BOT_TOKEN` に注意してください。

## データベース

EF Core の Migration は使わず、テーブル定義は手動で管理しています。
アプリの変更に伴う DDL は `JAFleet/Ddl/` に置き、デプロイ前後の適切なタイミングで手動適用します。

`alter_access_log_add_visitor_id.sql`（アクセス集計用の `visitor_id` / `route_key` 列と
期間検索用インデックス）は**デプロイ前**に実行してください。列が無いまま新しいアプリが動くと
アクセスログの INSERT が落ち、記録が止まります（書き込みはバックグラウンドのため、
画面にはエラーが出ません）。

主なテーブル・ビューは `JAFleet.Commons` の `JAFleetContext` を参照してください
（`aircraft`、`aircraft_view`、`aircraft_history`、`aircraft_photo`、`working_status`、
`airline`、`type`、`type_detail`、`code`、`search_condition`、`access_log`、`log`、
`scheduler_def`、`jcab_import_session` など）。
