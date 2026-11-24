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
| 機体詳細 | `/AD/{レジ}`（`/AircraftDetail/{レジ}`） | 1 機の詳細と更新履歴。`/ADN`（ヘッダ無し）、`/ADNB`（ヘッダ無し＋戻る）、`/ADE`（手で登録した写真の埋め込み）の派生あり |
| 検索 | `/Search` | 航空会社・型式・運用状況・Wi-Fi・登録年月・抹消年月などの条件検索。条件は名前を付けて保存でき、`?sc={キー}` で共有できる |
| 非稼働情報 | `/NotWorkingInfo` | 一定期間飛んでいない機体の一覧。公開メニューへの表示はサイト設定で切り替えられる（隠してもページ自体は開ける） |
| 稼働チェックログ | `/WorkingCheckLog` | 稼働状況が変化した機体の記録 |
| 問い合わせ | 各ページのモーダル（`wwwroot/message.html`）→ `POST /Message/Send` | 送信内容を Slack へ通知し、`message` テーブルへ記録 |
| LINE | `/Line` | 公式アカウントへリダイレクト（アクセスをログに記録） |
| 死活監視 | `/Check` | `aircraft_view` の件数を返す。アクセスログ・自動ログインの対象外 |

#### トップページ

`/` は `Views/Home/Index.cshtml` と `wwwroot/css/home.css` で描いています。

- **機体記号で開く**: 入れたレジを `/api/Reg/{レジ}` で確かめてから詳細（`/AD/{レジ}`）へ移る。数字で始まるときは `JA` を補う
  （`381A` → `JA381A`）。見つからなければその場で知らせ、検索へ案内する
- 「条件で検索」は検索画面へのリンク。「航空会社と型式で開く」は従来のクイックリンク（`/Aircraft/Airline/{会社}/{型式}`）
- **航空会社から探す**: グループごとのカードに航空会社と**機数**を並べる。機数は一覧を開いたときの初期表示と同じく
  抹消済（運用コード 8）を除いた数で、`HomeController.Index` が `aircraft_view` を航空会社・型式ごとに数える。
  「その他の航空会社」は社数が多いので、768px 以上では下の段で横幅いっぱいに使う
- **型式から探す**: 型式をメーカーごとにチップで並べ、機数を添える。抹消済しか無い型式はグレーにする
  （スマホではナビバーの「型式別」メニューが長くて選びにくいため）
- 運営者の表記、X のタイムライン（管理者には出さない）、管理者向けのバージョン表示は従来どおり

#### ナビバー

共通レイアウトのナビバーは `Views/Shared/_SiteNav.cshtml`（見た目は `wwwroot/css/site.css` の「ナビバー」）です。
上に固定した高さ 56px の白いバーで、文字は各画面と同じ 14px・太字・slate 系の色に揃えています。

| 幅 | 並び |
| --- | --- |
| スマホ・タブレット（〜991px） | ロゴ・検索ボタン・☰ だけを出す。☰ でバーの下にメニューを開き、そのあいだは本文を暗くしてスクロールを止める（暗い所を押すと閉じる）。航空会社グループと型式はその場で開く（アコーディオン）。LINE・X・お問い合わせはメニューの下に名前付きで並べる |
| 992〜1199px | 1 段にメニューを並べる。メニュー名は短く（「ANA」「JAL」「その他」「型式」「非稼働」） |
| 1200px〜 | 1 段にメニューを並べる。メニュー名も省かない |

- 航空会社グループのドロップダウンは、先頭に「〇〇のすべて」（グループ全体の一覧）を置き、その下に航空会社を並べる
- 型式のドロップダウンはバーの幅いっぱいに開き、メーカーごとに 1 行で型式を並べる（40 を超えるので縦に長いリストにしない）。
  メーカー表に無いメーカーは「その他」にまとめて最後に置く
- いま見ているページに当たるメニュー（検索・航空会社グループ・型式・非稼働情報）と、ドロップダウンの中の航空会社・型式を青くする
- LINE・X・お問い合わせ・管理コンソール・ログアウトは、PC では右端のアイコン（名前は title）。ログインのリンクは従来どおり画面を 5 回たたくと出る（`a.js`）
- バーの幅は Bootstrap の `.container` と同じにして、ロゴの左端を本文の左端に揃えている
- `body` の上の余白は 64px（バー 56px ＋ 8px）。開いたメニューはバーの外（`position: absolute`）に出すので、
  一覧や詳細がナビバーの高さを測って位置を合わせる処理（`.navbar-fixed-top` を見る）には影響しない
- 開閉は `wwwroot/js/jf-ui.js`（Bootstrap 3 の collapse / dropdown の代わり。`data-toggle` の書き方はそのまま）で行う。メニューを足すときは 992px で 1 段に収まるかを確かめること

#### 機体一覧の画面

航空会社・型式ごとの一覧（`/Aircraft/...`）と検索結果（`/Search`）は、共有の部品
`wwwroot/js/fleet-list.js` と `wwwroot/css/fleet-list.css` で描いています（DataTables は使っていない）。
1 機ぶんの HTML は 1 つで、CSS だけで幅に合わせて並びを組み替えます。どの幅でも横スクロールはしません。

| 幅 | 並び |
| --- | --- |
| スマホ（〜599px） | 1 機 2 行。1 行目にレジ・機種・Wi-Fi・運用、2 行目に航空会社・製造番号・登録年月日・コンフィグ。特別塗装・備考があれば 3 行目 |
| タブレット（600〜991px） | 表。「機体記号/製造番号」「機種/航空会社」「登録年月日/運用」「特別塗装/備考」を上下 2 段に重ねる |
| PC（992px〜） | 表。1 機 1 行で全項目を並べる。長い特別塗装名・備考は「…」で省略し、全文はマウスを載せると出る。1199px までは備考を特別塗装の下へ回す |

- 1 社だけの一覧（`/Aircraft/Airline/{id}` など）では、全行同じになる航空会社の列を出さない
- 見出しを押すと並べ替え（初めの向き → 逆向き → 標準の並び）。スマホは見出しの代わりにプルダウン。
  登録年月日・コンフィグ（席数の合計）・Wi-Fi は新しい・多い順から始まる。値の無い機体は常に後ろ
- 絞り込み欄は、レジ・試験飛行用レジ・機種・航空会社・製造番号・登録年月日・運用・コンフィグ・特別塗装・備考を
  部分一致で探す（空白区切りで AND）。サーバには問い合わせない
- 行のどこを押しても機体詳細が開く（中のリンクはそれぞれの動き）。幅 700px 以上は画面の上に重ねて開き、Esc・背景・右上の × で閉じる。レジは `/AD/{レジ}` への普通のリンクなので、
  Ctrl＋クリックや中クリックで別タブにも開ける。管理者には製造番号が単票編集へのリンクになる
- データ更新から 1 週間以内の機体は、ピンクの背景と左の線で印を付ける。退役済の機体は文字を薄くする
- **一覧に抹消済みの機体が 1 機でもあるときだけ「抹消年月日」の列を出す**（退役機を含めた一覧や検索結果など）。
  無いときは列も並べ替えの選択肢も出さない。PC は登録年月日の右、992〜1199px とタブレットは登録年月日の下に重ね、
  スマホは 2 行目に「抹消 yyyy/MM/dd」と出す。Excel 出力も同じで、列があるときだけ「抹消年月日」を書き出す
- Excel 出力は**表示中の行を表示中の並びで**書き出す（絞り込み・並べ替えが反映される）。
  ライブラリ（SheetJS）は一覧を開くたびに読まず、ボタンを押したときだけ cdnjs から読み込む
- 一覧の見出しは固定ナビバーの下に貼り付くよう、ナビバーの高さを測って位置と `body` の余白を合わせている
  （ナビバーは 1 段に収めてあるが、フォントの違いなどで折り返したときの備え）

#### 機体詳細の画面

`/AD/{レジ}`（とヘッダ無しの `/ADN`、戻る付きの `/ADNB`）は `Views/AircraftDetail/Index.cshtml` と
`wwwroot/css/aircraft-detail.css` で描いています。

| 幅 | 並び |
| --- | --- |
| スマホ・タブレット（〜991px） | 1 列。見出し → 現在の情報 → 写真 → 更新履歴 |
| PC（992px〜） | 左に現在の情報と更新履歴、右に写真。写真はスクロールしても画面に残る |

- 見出しはレジ・運用バッジ・機種・航空会社。`/AD` では、グループ・航空会社・型式・航空会社×型式の一覧へのリンクを並べる。
  管理者には「編集」（単票編集）と「履歴を編集」（`/AircraftHistory`）のボタンが出る
- 「現在の情報」は `aircraft_view` の 1 行をサーバ側で描く。コンフィグはクラスごとの席数と合計、Wi-Fi は「あり」「なし」の言葉で出す
- 「更新履歴」は `/api/AircraftWithHistory/{レジ}` を読み、**版ごとに変わった項目だけ**を「前 → 後」で並べる
  （どの項目が変わったかは API の `〇〇Changed` の印。1 つ古い版と比べて立っている）。いちばん古い版は「記録開始」として主な項目を並べる。
  運用・機種・コンフィグ・特別塗装など一覧に出す 9 項目以外（試験飛行用レジなど）だけが変わった版は「変更はありません」と出る。
  従来の全項目の表は「全項目を表で見る」を開くと出る（変わった欄に印）
- 写真の枠には **Flightradar24 の機体ページ**（`https://www.flightradar24.com/data/aircraft/<レジの小文字>`）を iframe で埋め込む。
  写真（撮影者の表記つき）と飛行履歴がそのまま見られる。見出しは「写真・飛行履歴」で、「Flightradar24で開く」のリンクと、
  枠の下に「表示: Flightradar24」の出典を出す（FR24 の規約 8.2 は公開する場合に FR24 の表記を求めている）。
  手で登録した写真のページ（`aircraft.link_url` → `/ADE/{レジ}`）がある機体だけはそちらを出す
  - FR24 の規約には埋め込み（frame / embed）を禁じる条文は無く、FR24 のページも埋め込みを拒んでいない（2026-11-24 に確認）。
    読み込むのは閲覧者のブラウザなので、こちらのサーバーから FR24 を自動で取りに行くことはない（規約 2.3 の自動アクセスに当たらない）
  - ただし Cloudflare のボット対策の確認画面は埋め込みを拒む（`X-Frame-Options: SAMEORIGIN`）ので、確認画面が出る閲覧者には
    枠が空になる。そのときは「Flightradar24で開く」から直接開いてもらう
  - FR24 の機体ページが無いレジ（予約登録だけの機体など）は、枠の中に FR24 の「見つからない」ページが出る
- **写真の iframe は、枠が画面に入ってから読み込む**。JetPhotos・FR24 のページは読み込むと中の要素へフォーカスを当てることがあり、
  枠が画面の外にあるうちに読み込むと、ブラウザが枠まで勝手にスクロールしてしまう（スマホでは開いた途端に機体の情報が
  見えなくなっていた）。PC では枠を最初から画面に収まる高さにしてあるので、開くとすぐ読み込む

#### 検索画面

`/Search` の条件フォームは `Views/Search/Index.cshtml` と `wwwroot/css/search.css`、結果は機体一覧と同じ部品で描いています。
送る項目名と `/Search/DoSearch` などのサーバ側の受け口は変えていません。

| 幅 | 並び |
| --- | --- |
| スマホ（〜599px） | 条件は 1 列。「クリア」「条件を保存」「検索」のバーをカードの下端に貼り付け、長い条件でもスクロールせずに押せる |
| タブレット（600〜991px） | 条件を 2 列 |
| PC（992px〜） | 条件を 3 列（3 段に収まる） |

- 複数選べる項目（航空会社・型式・運用状況・Wi-Fi）と保存済みの条件は自前の部品 `wwwroot/js/jf-multiselect.js`
  （以前の bootstrap-multiselect の代わり。`jf-multiselect.min.js` は BuildBundlerMinifier が作る）。
  作る DOM は bootstrap-multiselect と同じクラス名なので、`search.css` の `.sx-ms` / `.multiselect-container` がそのまま効く
  - ボタンは「指定しない」→ 2 つまでは名前 →「N 件を選択」→ 全部なら「すべて」
  - 航空会社・運用状況・型式はグループ名を押すとまとめて選べ（もう一度押すと外れる）、右の ▼ で畳める（型式は最初から畳んである）
  - 型式と保存済みの条件は名前で絞り込める。絞り込み中は畳んだグループの中も出す。
    PC では開くと絞り込み欄にフォーカスし、スマホではしない（キーボードで一覧が隠れないように）
  - 開け閉めは `jf-ui.js` のドロップダウンに任せている（外を押す・Esc で閉じる）
  - 選んだ値は元の `<select>` に入れ、人が選び直したときだけネイティブの change を起こす（スクリプトからの `setSelected` では起こさない）。
    送る値や要約は `<select>` から読む
- 1 つだけ選ぶ項目（登録年月の範囲・特別塗装・備考）は素の `select` にした（スマホでは OS の選択画面が出る）
- 特別塗装・備考のキーワード欄は「キーワード指定」を選んだときだけ出る。キーワード欄で Enter を押しても検索する
- 「登録年月」「抹消年月」は日付の範囲で絞る。「と等しい」はその月、「以前」「以降」はどちらもその月を含む。
  抹消年月を指定すると抹消年月日の無い機体は出ない
- 抹消年月は既定値（空・「と等しい」）のとき保存する条件の JSON に出ない（`DefaultValueHandling.Ignore`）ので、
  足す前に保存した条件や共有した `?sc=` のキーは変わらない
- **幅 767px 以下では、結果が 1 件以上出たら条件を畳み**、選んだ条件をチップで要約する（「条件を変更」で開く）。
  `?sc=` で開いたときも同じ。PC・タブレットでは畳まない
- 保存済みの条件（`?sc=`）から開いたときに管理者の製造番号リンクが出ていなかったのを直した
  （条件を復元するときに画面用のモデルを作り直していて、先に入れた管理者の判定が消えていた）。
  特別塗装「キーワード指定（履歴含む）」の条件を `?sc=` で開くとキーワード欄が使えなかったのも直した

#### 非稼働情報の画面

`/NotWorkingInfo` は `Views/NotWorkingInfo/Index.cshtml` と `wwwroot/css/not-working.css` で描いています（機体一覧と同じ見た目）。
データは従来どおり `/NotWorkingInfo/GetInfo?fromDate=` から取り、最終稼働日時の古い順（止まっている期間が長い順）に並べる。

- 最終稼働日時に「〇日前」を添える。7 日以上はオレンジ、30 日以上は赤
- 区間は空港コードを太字にし、PC では都市名を小さく添える。航空会社も出す（`GetInfo` が `aircraft_view` から返す）
- 「最終稼働日が〇以前」の初期値は 3 日前で、サーバ側で入れる
- 絞り込み欄は、レジ・機種・航空会社・便名・空港・状況を部分一致で探す。行を押すと機体詳細、「FR24」で Flightradar24 の機体ページを開く
- Excel 出力は表示中の行を書き出す（機体一覧と同じ `FleetList.exportXlsx`）
- 幅は PC で 1 機 1 行、タブレットでは状況を区間の下へ回し、スマホでは 1 機 2 行

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

`registerDate`（登録年月日）と `deregisterDate`（抹消年月日）は `"2019/03/15"` の形の文字列で返します。
DB では `date` 型ですが、文字列だったころの API と同じ形を保つため、`Program.cs` で JSON の変換
（`JAFleet.Commons.Util.SlashDateOnlyJsonConverter`）を登録しています。

### 管理機能

管理者向けの機能です。各画面は Cloudflare Access でのログイン（`AdminAuth.IsAdmin`）を前提としています。

| 機能 | パス | 説明 |
| --- | --- | --- |
| 管理コンソール | `/Admin` | 以下の各機能への入口。レジ検索、編集・詳細への直行 |
| 単票編集 | `/E/{レジ}` | 機体情報の新規登録・更新。更新時は `aircraft_history` へ退避。登録ボタンでは履歴を残すかどうかを確認する。「予約日」を入れると、その日に反映する予約になる |
| 機体更新の予約 | `/AircraftReservation` | 特別塗装・運航の開始日に合わせて反映する予約の一覧と取消（[機体更新の予約](#機体更新の予約)） |
| 更新履歴の編集 | `/AircraftHistory?reg={レジ}` | 機体詳細に出る更新履歴（`aircraft_history`）の修正・削除 |
| 同一型式の一括登録 | `/BulkRegister` | 「レジ / 登録年月日 / 製造番号」をタブ・カンマ・空白区切りで貼り付けてまとめて登録。登録年月日が月まで（`2026/08`、`2026/08/xx`、`R8.8`）ならその月の末日にし、日付として読めない行はエラーにする |
| 航空局 Excel 取込 | `/JcabImport` | 国土交通省航空局から届くパスワード付き Excel を、Gmail から直接またはアップロードで取り込む |
| コンフィグマスタ | `/SeatConfig` | 航空会社・型式ごとの座席仕様（コンフィグ）を登録・更新・削除する |
| 特別塗装ニュース | `/LiveryNews` | Aviation Wire の RSS から Gemini が作った特別塗装の更新候補を承認・却下する |
| マスタ再読込 | `/ReloadMaster` | 航空会社・型式などのマスタとサイト設定をメモリへ読み直す |
| 定期実行ジョブ | `/Scheduler` | Quartz のジョブの有効・無効と cron 式を変更する |
| サイト設定 | `/SiteSetting` | 稼働状況の取得元（adsb.lol / Flightradar24）と、公開メニューの「非稼働情報」の表示・非表示を切り替える（`site_setting` テーブル）。保存するとその場で反映される |
| バッチ手動実行 | `/Batch/RefreshWorkingStatusAndPhoto`<br>`/Batch/RefreshPhoto` | 定期ジョブと同じ処理を手動で起動 |
| アクセスログ | `/log`（本日）、`/logy`（昨日） | 閲覧・検索の記録 |
| アクセス集計 | `/AccessStats` | `access_log` を集計して PV・訪問者・人気ページなどを表示 |

#### 航空局 Excel 取込の流れ

1. `/JcabImport` でファイルを取り込む。入口は 2 つある。
   - **Gmail から**（通常はこちら）。届いているメールの一覧から選んで「取込」を押すだけ。
     パスワードに関する入力は要らない（後述）
   - **アップロード**。手元のファイルを選び、送信年月日と内線番号からパスワードを組み立てる。
     受け取りから日が空いていても開けるよう、送信日を数日分遡って自動で試す。
     組み立てに使う内線番号は `code` テーブルに持たせ、リポジトリには置かない
2. NEW / DEL / TRAN / CNG / RESERVATION / CANCEL の各シートを解析し、レジ単位のプレビューを作る。
   プレビューは「そのまま取り込める」「人の判断が要る」「移転登録」「対象外」に分類される。
3. 途中で保存すると `jcab_import_session` に復号済みファイルと編集内容（JSON）が残り、後から再開できる。
   再開時は Excel を解析し直したうえで編集内容を被せるため、最新のマスタ・機体情報と突き合わせられる。
4. 実行すると `AircraftStore` 経由で 1 トランザクションにまとめて登録・更新する。

プレビューの「登録年月日」の初期値は、シートによって取る場所が違います（`ImportPreviewBuilder.SuggestRegisterDate`）。

| シート | 初期値 |
| --- | --- |
| NEW | 登録年月日 |
| RESERVATION | F 列 **2 行目の納入予定年月日**。1 行目の予約年月日は予約を受け付けた日で、機体の登録年月日ではないので使わない。予定は月までのことが多く、そのときは**その月の末日**にする（登録年月日は日付で持つので、月までの登録はできない） |
| TRAN / CNG / DEL | JAFleet の現在値を残す（JAFleet に無ければ最新の日付） |

予約登録で納入予定年月日が空・読めないときは、予約年月日へ逃げずに初期値を空にし、「人の判断が要る」に回します。

抹消登録（DEL）のレジは、抹消の日付を**抹消年月日**の欄に入れます（以前は備考に「yyyy/MM/dd抹消」と書いていた）。
新規・予約で登録し直したレジは抹消年月日を空にします。備考には触りません。
登録年月日・抹消年月日が日付として読めない行は、取込の実行時にその行だけ登録せずに理由を出します。

航空会社の初期値は、Excel の所有者名から推定します（`ImportPreviewBuilder.MatchAirline`）。
所有者名は「日本航空株式会社」「株式会社エア・ドゥ」のように、マスタの名称の前後に会社の種類が付いた形で来るので、
**会社の種類（株式会社・有限会社・合同会社・学校法人など）を前後から外したうえで、マスタの名称が先頭から一致するもの**を
採ります（複数当たれば長い名称を優先。ANA と ANAウイングス なら ANAウイングス）。
以前は「名称を含んでいれば一致」だったため、「中日本航空株式会社」が「日本航空」に当たって JAL と推定されていました
（2026 年 10 月分の JA848N）。マスタには ANA・JAL・RAC のような 3 文字の名称もあり、英語の社名の途中でも誤爆しうるため、
先頭一致にしています。リース会社が所有者の機体は、これまでどおり推定できず人が選びます。

##### 同じグループの行へまとめて反映する

航空会社は同じ機種をまとめて発注し、連番のレジを付けます（2026 年 10 月分なら JA01EA〜JA29EA の
E190-E2 予約 28 機など）。プレビューでは、同じ機材のまとまりと見なした行に「🔗 JA##EA ×28」の印を付け、
**ある行で詳細型式を選ぶ・チェックを入れる（外す）と、同じグループでレジが後ろの行にも同じ値が入ります**。

- **グループの条件**は、レジの形（数字だけを `#` にしたもの。JA01EA → `JA##EA`）・Excel の型式・所有者が
  すべて同じこと（`ImportPreviewBuilder.AssignGroups`）。2 行以上あるときだけ印が付く。
  レジの形だけでは粗く、既存機では `JA###A` が 6 社 18 型式、`JA###J` が 4 社 12 型式にまたがる。
  10 月分でも `JA##XJ` に、JAL の A350-900 予約 20 機と、リース会社への A350-941 の移転（JA14XJ）が混ざっていた。
  型式と所有者の表記ゆれ（半角カナ・中黒など）は `TextUtil.Normalize` で吸収する。型式が読めない行は束ねない
- **反映するのは後ろの行だけ**（Excel のフィルダウンと同じ）。途中から仕様が変わるまとまりは、
  変わり目の行で選び直せば、そこから後ろだけが変わる。前の行には触らない
- 反映するのは**詳細型式とチェックだけ**。航空会社・運用状況・登録年月日などは行ごとに違いうるので触らない
- 「対象外」へ移した行には反映しない（外した意図を優先する。元の行も対象外なら反映する）
- 詳細型式をモーダルから新規登録したときも、同じように後ろの行へ反映する
- 反映した行の数は、元の行に数秒だけ「→ 後ろの 27 機にも詳細型式を反映しました」と出る

プレビューの「取込を実行」と「一時保存」は、チェックの有無に関係なく**全レジの入力欄**
（折りたたんでいる「対象外」も含む）を送ります。1 レジあたり 9〜11 項目になるため、ASP.NET Core の
既定のフォーム項目数の上限（1024）では約 113 レジで読めなくなります（2026 年 10 月分の 123 レジで発生。
MVC はフォームを読めないと引数を null のままアクションを呼ぶので、NullReferenceException になっていた）。
この 2 つのアクションだけ `[RequestFormLimits]` で上限を `JcabImportConstant.FORM_VALUE_COUNT_LIMIT`
（10000、約 900 レジ分）に引き上げています。それでも超えたときは、何も書き込まずに取込画面へ戻して理由を出します。

押してから画面が切り替わるまで数秒かかる操作（Gmail からの取込、Excel の解析、再開、取込の実行）は、
画面全体をぐるぐるで覆います（共有部分ビュー `_BusyOverlay` と `_BusyOverlayScript`。待たせる要素に
`data-busy="〇〇しています…"` を付けるだけ）。覆っている間はボタンを押せないので、**取込の二度押しで
一時保存が 2 つできる事故も防げます**。ファイルを返すだけで画面が切り替わらないダウンロード系には
付けていません（覆ったまま戻らなくなるため）。

##### Gmail からの取込

メールそのものに必要な情報が全部載っているため、**人が入れる値は無くなります**。

| メールの中身 | 使い道 |
| --- | --- |
| 送信日時 | パスワードの「送信年月日」。日本時間で見る |
| 件名の `2026年8月分` | 一覧に出す対象月。Excel を開く前の目安 |
| 件名の `(再送)` `【再送】` | 再送の印。再送分のパスワードは**元のメールの送信年月日**のままなので、ここでも送信日を遡って試す |
| 本文の `（内線48146）`、宛先の `register48146@` | 内線番号。`code` テーブルの登録値で開けなかったときに使う |

- 候補の検索式は `JcabImportConstant.GMAIL_QUERY`。差出人は担当官個人のアドレスで**異動のたびに変わる**ため
  ドメイン（`from:mlit.go.jp`）で見て、件名の「航空機登録情報」で絞り、`in:inbox` で受信トレイだけに限る。
  **取り込んだらアーカイブする運用**を前提に、受信トレイに残っている＝まだ取り込んでいない、としている。
  済んだ分を掘り返したいときは画面の入力欄から `in:inbox` を外す（0 件のときはその案内が出る）。
  入力欄の変更は一時的なもので保存はしない（既定値を変えるならコードを直す）
- パスワードは **登録済みの内線番号 → メールから読んだ番号** の順に、送信年月日を
  `PASSWORD_RETRY_DAYS` 日分遡って総当たりする。メールの番号で開けた場合は、
  担当官が代わったと見て `code` テーブルを**その番号に更新する**（次回からは 1 回で当たる）。
  本文の charset が ISO-2022-JP だと .NET 本体では読めず「内線」が文字化けするが、
  宛先アドレスは ASCII なのでそちらから拾える
- `jcab_import_session.gmail_message_id` にどのメールから作ったかを残す。一覧では
  そのメールを「作業中・再開」と出して、同じメールから一時保存を二重に作らないよう誘導する
- Gmail の **`attachment_id` はメールを取得するたびに変わる**（同じメール・同じ添付でも別の値になる）。
  そのため一覧と取込の間で往復させられない。どの添付かは取り直しても変わらない
  `partId`（MIME の木の中の位置）で指し、本体の取得にはそのとき得た `attachment_id` を使う
- Gmail を読むのは**「探す」を押したときだけ**。結果はどこにも記録せず、押すたびに取り直す。
  画面を開いただけでは読まない（アップロードで取り込みたいだけのときに無駄に API を叩かないため）。
  一覧は部分ビュー `_GmailList`（`GET /JcabImport/GmailList`）で、画面の枠とは分けてある。
  一覧の取得は検索 1 回＋1 通ずつの取得なので、`GMAIL_SEARCH_MAX` 件なら API 呼び出しは最大 11 回
- スコープは **`gmail.readonly` だけ**。アプリからメールボックスを書き換えることはできないので、
  取込済みの管理は上記のとおり JAFleet 側の DB で行う。ラベルも付けない
- 未設定でも起動する。その場合は Gmail の枠に設定方法だけが出て、アップロードで従来どおり取り込める

###### Gmail API の設定

Google の同意画面は人がブラウザで通る必要があるため、**手元でこのアプリを動かして 1 回だけ認可**し、
得た refresh token を本番へ持っていきます（n8n の「Connect my account」と同じこと。n8n も画面に
入れるのは ID とシークレットだけですが、同意の結果として受け取った refresh token を自分の DB に
持っています）。refresh token は**クライアント（`client_id`）に紐づく**ので、手元で取ったものを
本番でそのまま使えます。要求するスコープは `gmail.readonly` だけ。

1. Google Cloud Console で **Gmail API を有効化**する（n8n で Gmail を使っているプロジェクトでよい。
   同意画面が「テスト」のままだと refresh token が 7 日で失効するため、**公開（本番）**にしておく）。
2. OAuth クライアント ID を **ウェブ アプリケーション**として作り、承認済みのリダイレクト URI に
   `http://localhost:5000/JcabImport/GmailAuth` を登録する（localhost は http でよい。
   完全一致が要るので、ポートまで合わせる）。
3. クライアント ID とシークレットを `launchSettings.json` の `GMAIL_CLIENT_ID` /
   `GMAIL_CLIENT_SECRET` に入れて手元で起動し、**`/JcabImport/GmailAuth`** を開いて
   「Google の同意画面へ」を押す。同意すると同じ画面に戻り、refresh token が表示される。
4. 得た 3 つをサーバの `pass` に入れ、`ja-fleet.service` の `ExecStart` で環境変数として渡す
   （他のシークレットと同じ運用。詳細は `ja-fleet.noobow.me` リポジトリ）。

`/JcabImport/GmailAuth` は **`ASPNETCORE_ENVIRONMENT=Development` のときだけ開く**
（`IsDevelopment()` と `AdminAuth.IsAdmin` の両方を見て、外れていれば `NotFound`）。
refresh token を画面に表示する経路を本番に残さないためです。表示するだけで、アプリは保存しません。

```
pass insert ja-fleet.noobow.me/ja-fleet/gmail_client_id
pass insert ja-fleet.noobow.me/ja-fleet/gmail_client_secret
pass insert ja-fleet.noobow.me/ja-fleet/gmail_refresh_token
```

```
export GMAIL_CLIENT_ID=$(pass ja-fleet.noobow.me/ja-fleet/gmail_client_id); \
export GMAIL_CLIENT_SECRET=$(pass ja-fleet.noobow.me/ja-fleet/gmail_client_secret); \
export GMAIL_REFRESH_TOKEN=$(pass ja-fleet.noobow.me/ja-fleet/gmail_refresh_token); \
```

- refresh token は**環境変数のまま**使い、DB にもディスクにも書かない。アクセストークンは
  プロセス内のメモリに有効期限まで持つだけ（`GmailClient`）。
- アカウントのパスワード変更やアクセス権の取り消しで refresh token が失効すると、
  画面に `invalid_grant` が出る。その場合は 3 をやり直して `pass` を入れ替える。

#### 更新履歴の編集

`/AircraftHistory` は `aircraft_history` の画面です。履歴は単票編集や航空局 Excel 取込で機体を
更新したときに `AircraftStore` が自動で積むもので、**間違った内容のまま積まれてしまった履歴を
直す・消すため**だけにあります。入口は管理コンソールのレジ検索の「更新履歴」と、単票編集の下の「更新履歴」。

- レジを入れると、いまの機体情報（比較用。ここからは直せない）と履歴が SEQ の降順で並ぶ
- **一覧がそのまま入力欄**になっていて、何行でも直してから「まとめて保存」で一度に保存する。
  実際に変わった行だけ更新し、1 件でも入力に不備があればどの行も保存しない
- 一覧に出していない項目（リンク URL・テストレジ・整備通知・作成日時・実更新日時）は
  行の「⋯」で開く。開いていなくても保存の対象になる
- 詳細型式の選択（と型式・詳細型式のその場登録）、コンフィグの選択・その場登録は単票編集と同じ部品を使う
- 日時は秒までしか出していないので、**秒まで一致していれば元の値を残す**。
  そうしないと、触っていない行まで秒未満が切り捨てられて更新されてしまう
- 履歴は機体を更新した結果として積まれるものなので、普通の「追加」は置いていない。
  代わりに**リカバリ用の「いまの内容で履歴を作る」**がある。いまの `aircraft` をそのまま
  コピーして 1 件積むだけで、`aircraft` 側は何も変えない（`AircraftStore.AddHistory` を使い回す）
- 削除と履歴作成は、押す前に画面内のダイアログで確認する
- **SEQ** はレジごとの連番で、機体詳細の履歴はこの順に並ぶ。削除しても詰めない
  （並び順にしか使わないので飛んでいても表示は変わらない）。同じレジで重複だけはさせない
- **詳細型式は必須**。`aircraft_history_view` が `type_detail` と突き合わせているため、
  未設定だとその行が機体詳細の履歴から落ちる。一覧ではそういう行に「マスタ外」と出す
- 一覧の表示名はマスタから引いている。`aircraft_history_view` を使うと、
  まさに直したい「マスタ外」の行が落ちてしまうため

##### 「履歴を作成しない」を間違えたときのリカバリ

`AircraftStore.Store` は **履歴を残すときだけ `update_time` を進める**（残さないときは据え置き）。
これを踏まえると、チェックの付け間違いは次のように直せます。

**チェックを入れ忘れた（余計な履歴が積まれた）** — 何も失われていないので完全に戻せる。

1. 増えた行（SEQ 最大、履歴登録日時が直近）の**更新日時をメモ**する。
   これが上書きされる前の `aircraft.update_time`
2. その行を削除する
3. 単票編集で更新日時にメモした値を入れ、「履歴を作成しない」に**チェックを入れて**保存する

戻らないのは `actual_update_time` だけ（単票編集では読み取り専用）。毎晩のジョブが書き換える値なので実害は無い。

**チェックを外し忘れた（履歴が積まれなかった）** — 更新前の内容はどこにも残らない
（`log` テーブルは閲覧系だけで、編集は記録していない）。**内容を覚えている場合のみ**直せる。

1. 単票編集のいまの更新日時をメモする（まだミス前の値のまま）
2. 更新履歴の画面で**「いまの内容で履歴を作る」**を押す（器だけ作る）
3. できた行の中身を更新前の値に書き換え、履歴登録日時＝ミスした保存の時刻、
   更新日時＝1 でメモした値にして保存する
4. 単票編集（チェック入り）で `aircraft.update_time` をミスした保存の時刻に直す

覚えていない場合は DB のバックアップから引くしかない。

#### 単票編集の画面

`/E/{レジ}` は PC・タブレット・スマホの幅に合わせて組み替わります（`Views/E/Index.cshtml`）。
登録年月日と抹消年月日は日付の入力欄（カレンダーで選べる）です。抹消年月日は備考ではなくこの欄に入れます。

| 幅 | 並び |
| --- | --- |
| スマホ（〜599px） | 1 列。入力欄は iOS が拡大しない 16px |
| タブレット（600〜991px） | カードは縦に並べ、カードの中の項目を 2 列 |
| PC（992px〜） | カードを 2 列（左に機体・運用、右に備考・リンクと保存のしかた） |

- 項目は「機体」「運用」「備考・リンク」「保存のしかた」のカードに分けている
- 登録ボタンは**画面の下に固定したバー**にあり、長い画面でもスクロールせずに押せる。
  バーには変えた項目の数と、予約・「履歴を作成しない」の状態が出る。予約日を入れるとボタンが「予約する」になる
- 開いたときから値を変えた項目には「変更」の印が付き、登録の確認ダイアログにも変えた項目が並ぶ
- **既存の機体ではレジを変えられない**（主キーなので、変えると存在しない行の更新になって保存に失敗していた）。
  新規登録のときだけ入力できる
- `AircraftStore.Store` は機体全体を更新扱いにするので、**`Aircraft` の全項目を送る**必要がある。
  作成日時・実更新日時は読み取り専用の入力欄として持ち回っている（消すと保存で NULL になる）
- 固定ナビバーが `body` の余白（64px）より高いときは、この画面だけ余白を合わせて広げている
  （ナビバーは 1 段に収めてあるが、フォントの違いなどで折り返したときの備え）
- 「📋 リンク」と登録時の詳細ページ URL のコピーは、ブラウザ標準の Clipboard API（`navigator.clipboard.writeText`）で行う
  （clipboard.js は使わない）。https と localhost でしか使えない。アプリ内ブラウザなどで書き込みを拒否されたときは
  `document.execCommand('copy')` で写す。登録時は書き込みを待ってから送信する（返事が無くても 1 秒で送る）
- 詳細型式のピッカーとコンフィグのその場登録は、これまでどおり共有部品
  （`_TypeDetailPicker` / `_SeatConfigQuickAdd`）を `Aircraft_TypeDetailId` などの同じ id で使う

#### 機体更新の予約

特別塗装や運航の開始日が先に決まっているとき、**その日に機体情報を変えるよう予約**できます
（`aircraft_reservation`、`AircraftReservationStore`）。反映日が来ると日次ジョブ
`ApplyAircraftReservationJob` が反映し、結果を Slack へ知らせます。

- 予約の入口は 2 つ
  - **単票編集の「予約日」**。入れて登録すると、いまの機体は変えずに予約だけ積む。確認ダイアログも「予約」になる。
    新規登録では使えない（予約日の欄が出ない）
  - **特別塗装ニュースの承認**。運航開始日が明日以降なら、承認しても `aircraft` は変えずに予約する
    （[特別塗装ニュースの承認](#特別塗装ニュースの承認)）
- 予約日は**明日以降**だけ。当日分はもう日次ジョブが走ったあとかもしれないので、今日の分はそのまま登録する
- **持つのは変えた項目だけの差分**（`changes` に `[{"field":"SpecialLivery","before":…,"after":…}]` の JSON）。
  機体まるごとを持つと、予約から反映までに別の項目（運用状況など）を直したとき、反映した日に古い値へ巻き戻るため。
  差分は単票編集の画面の値といまの `aircraft` を比べて取る（空欄と NULL は同じとみなす）。
  レジ・作成日時・更新日時・実更新日時は予約の対象外（日時は反映時に `AircraftStore` が決める）
- 反映は単票編集と同じ `AircraftStore.Store` を通す。「履歴を作成しない」は予約にも持たせ、反映するときに効く。
  特別塗装ニュースからの予約は必ず履歴を残す
- 反映日の時点ですでに予約した値になっている項目は触らない。全部そうなら「差分なし」で終わり、機体は変えない
- ジョブは**反映日が今日以前の予約中のもの**を反映日・予約順に重ねていく。ジョブが止まっていた日の分も次の実行で拾う。
  1 件ずつ保存するので、1 件が失敗（機体が消えたなど）しても残りは反映され、失敗した予約は「失敗」として理由が残る
- `/AircraftReservation` では予約の確認・取消と、ジョブを待たずに**今日の分をいま反映**ができる。
  ジョブと画面の反映が重ならないよう `AircraftReservationStore.Processing` で見張る。
  反映ジョブがスケジューラに載っていなければ画面に警告が出る
- 単票編集の画面には、その機体の予約中の更新が上に並び、そこからも取り消せる
- 反映した機体は写真も取り直す（単票編集と同じ）

#### 型式・詳細型式のその場登録

型式（`type`）と詳細型式（`type_detail`）は、これまで行を足すには DB を直接触る必要がありました。
新しい機種が出てきたときに編集中の画面を離れなくてよいよう、**詳細型式を選ぶモーダルの中で
2 段とも登録できる**ようにしてあります（`TypeStore` / `TypeDetailStore`）。

- 入口は**詳細型式の選択モーダル**（`_TypeDetailPicker.cshtml`。単票編集・同一型式の一括登録・
  更新履歴の編集で使う共通部品）と、**航空局 Excel 取込のプレビュー**（`Preview.cshtml` のモーダル）。
  どちらも「一覧から選ぶ → 詳細型式を登録する → 型式を登録する」の 3 段をモーダル内で行き来する
- 詳細型式の登録画面で型式の選択肢に無いものは、**横の「＋」から型式ごと登録できる**。
  登録すると型式の選択肢に出て選ばれた状態で詳細型式の入力に戻るので、そのまま続けられる
- 型式コードは `type` の主キーで `/Aircraft/Type/{型式コード}` の URL にも入るため、
  **半角英数字と `-` `_` `.` だけ**通す。メーカー（`maker` から選ぶ）と表示順は任意。
  表示順を空欄にすると一覧の最後に並ぶ（PostgreSQL の昇順は NULL が後）
- **同じものが既にあってもエラーにせず、それを返して選ぶ**（型式はコード、詳細型式は名前で見る）。
  欲しいのは「その行」であって作ること自体ではないため。コンフィグのその場登録と同じ考え方
- 登録すると `MasterManager.ReadAll` も走るので、マスタ再読込を押す必要はない
- エンドポイントは `POST /Master/CreateType`・`POST /Master/CreateTypeDetail`（モーダル用）と
  `POST /JcabImport/CreateType`・`POST /JcabImport/CreateTypeDetail`（取込プレビュー用）。
  いずれも `AdminAuth.IsAdmin` を通らなければ `NotFound`

#### コンフィグマスタ

`/SeatConfig` は `seat_configuration` の画面です。ここに登録した行が、単票編集（`/E`）と
同一型式の一括登録（`/BulkRegister`）の「コンフィグ」の選択肢になります
（選択肢は航空会社と型式で絞り込まれるため、組み合わせごとに 1 行ずつ要ります）。
これまで行を足すには DB を直接触る必要がありました。

- 一覧は 1 行がそのまま入力欄で、その場で直して保存できる。航空会社・型式でも絞り込める
- 席数は `F`（ファースト）`C`（ビジネス）`PY`（プレミアムエコノミー）`P`（プレミアム・国内線）
  `J`（クラス J）`Y`（エコノミー・普通席）の 6 区分。**その区分が無ければ空欄**にする。
  0 を入れると「Y0」のように表示されてしまうため、0 以下は登録できない
- 航空会社・型式・席数がすべて同じ行は、選ぶときに区別できないので登録できない
- **削除できるのはそのコンフィグを使っている機体が 0 機のときだけ**。「使用」列が機体数で、
  1 機でも使っていれば削除ボタンを押せない（`aircraft.seat_config` が宙に浮かないようにするため）
- 保存・削除すると `MasterManager.ReadAll` も走るので、マスタ再読込を押す必要はない
- **単票編集（`/E`）と同一型式の一括登録（`/BulkRegister`）のコンフィグ欄の「＋」**から、
  その画面を離れずに 1 件足せる。
  航空会社と型式は入力中の内容（航空会社と詳細型式）で決まるので、入れるのは席数だけ。
  登録したものはその場で選択肢に出て選ばれた状態になる。同じ内容が既にあるときは、作らずにそれを選ぶ
  （詳細型式のその場登録と同じ考え方。`_SeatConfigQuickAdd.cshtml` と `POST /Master/CreateSeatConfig`）
- 座席図 URL（`seat_configuration.url`）は任意。いまはどの画面にも出していないが、
  `aircraft_view.seat_url` として取り出せる

#### 特別塗装ニュースの承認

`/LiveryNews` は、ニュースから特別塗装（`aircraft.special_livery`）を拾って更新する画面です。
けんけつノートのルーム情報チェックと同じ作りで、**機械が候補を作り、人が承認したときだけ DB が変わります**。

1. 日次ジョブ `CheckSpecialLiveryNewsJob` が Aviation Wire の RSS（`https://www.aviationwire.jp/feed`）を取得する
2. まだ判定していない記事だけを Gemini に投げ、「特別塗装機が新たに登場した記事か」を判定させる。
   本文は RSS に載らない（冒頭の抜粋だけ）ため、`url_context` で記事そのものを読ませる
3. 特別塗装と判定されたら、記事に出てきた機体ごとに更新候補（`livery_news_candidate`）を作り、Slack へ通知する
4. `/LiveryNews` で候補を確認し、承認すると `AircraftStore` 経由で `aircraft.special_livery` を更新する
   （単票編集と同じ処理を通るので、更新前の内容は `aircraft_history` に残る）。
   **運航開始日が明日以降なら、いまは変えずに[その日に反映する予約](#機体更新の予約)を作る**

- **RSS の取得には User-Agent が要る**。Aviation Wire は User-Agent が空のリクエストを 403 で返す
  （ヘッダの中身は見ておらず、何か入っていれば通る）。.NET の `HttpClient` は既定で User-Agent を
  送らないため、付けずに叩くと必ず 403 になる。値は `LiveryNewsConstant.USER_AGENT`
- **判定した記事は特別塗装でなかったものも `livery_news` に残す**。チェック済みの印なので、
  同じ記事を二度 Gemini に投げることはない（RSS は毎回同じ記事を返すため）
- 登録済みの特別塗装と同じ内容だった候補は、作りはするが**「差分なし」として処理済み**にする。
  同じ塗装の記事が何本も出ても未処理として溜まらない
- 記事からレジを読めなかったときも候補は作る。レジ欄を空のまま出すので、人が入れてから承認する。
  「JA926A（787-9）」のように型式までくっついてきた場合も読み取り失敗として空にする
  （誤ったレジを既定値にして、そのまま承認されるのを防ぐため）
- 塗装名とレジは**画面で直せる**。承認すると直した値が `aircraft` に入り、`livery_news_candidate` にも
  その値が残る。Gemini が出した元の値は `livery_news.gemini_result` に JSON のまま残る
- 却下すると機体情報は変えずに処理済みにする。押し間違いを戻せないので確認ダイアログを出す
- **運航開始日**（`livery_news_candidate.start_date`）も Gemini に記事から読ませる（就航日・初便の日。
  年が無ければ記事の公開日から補わせ、「12月上旬」のように日が決まっていなければ null）。
  画面で直せ、明日以降の日付が入っていると承認ボタンが「承認（予約）」になる。承認すると候補は「予約」になり、
  その日に日次ジョブが反映する。予約は `/AircraftReservation` で取り消せる。Slack 通知にも開始日を添える。
  **予約になるのは承認したときだけ**で、ニュースを取得しただけでは予約しない（人が承認したときだけ DB が変わる、のまま）
- 「いま確認する」はジョブを待たずに RSS を確認する。記事ごとに数秒待つため 1 回が数分かかることがあり、
  **バッチ手動実行と同じく裏で動かしてすぐ画面を返す**。終わると Slack に通知が飛ぶので、画面を読み直す。
  日次ジョブと重なって同じ記事を二重に投げないよう、実行中かどうかは `LiveryNewsChecker.Processing` で見張る
- **RSS に載るのは直近 10 本ほど**。1 日の記事数がこれを超えると、次の実行までに流れた記事は判定されない。
  取りこぼしが気になるなら `/Scheduler` で cron 式を数時間おきに変える。判定済みの記事は投げ直さないので、
  実行を増やしても Gemini の呼び出し回数は増えない
- 判定の線引きは `GeminiLiveryJudge.BuildPrompt` のプロンプトだけで決まる。外した判定が続くならここを直す。
  使うモデルは `LiveryNewsConstant.GEMINI_MODEL`
- `GEMINI_API_KEY` が未設定のときは、画面にその旨が出て「いま確認する」が押せない

##### 試すとき

特別塗装のニュースが実際に出るのを待たなくても、画面の一番下にある「🧪 お試し（テスト用）」から一連を通せます。
通る処理は日次ジョブとまったく同じ（`LiveryNewsChecker.SaveJudgementAsync`）なので、
ここで動いたならジョブでも同じように動きます。

| やりたいこと | 操作 | DB | Slack |
| --- | --- | --- | --- |
| 判定の線引きを見たい | 記事 URL（または本文）を入れて**判定だけする** | 触らない | 飛ばない |
| 一連を通したい | 記事 URL を入れて**判定して候補を作る** | 記事と候補を作る | 飛ぶ |
| 承認の反映だけ試したい | レジと塗装名を入れて**候補を手で作る** | 候補を 1 件作る | 飛ばない |

- 「判定だけする」は **Gemini の応答 JSON をそのまま画面に出す**。抽出されたレジが候補にどう入るか
  （読み取れずに空になるかどうか）も並べて出すので、プロンプトを直す目安になる
- **本文の貼り付けだけでも判定できる**ので、架空のニュースを書いて挙動を確かめられる
  （候補を作るほうは一覧から記事を開けるようにしたいので URL 必須）
- 「候補を手で作る」は Gemini を通さない。レート制限も課金も気にせず、承認 → `aircraft` の更新 →
  更新履歴 → 写真の取り直しだけを何度でも試せる。記事欄は「（手動で作った候補）」になり、
  URL が `manual:` で始まるため画面ではリンクにしない
- 試したものは「判定した記事」の**削除**で片付ける。記事と未処理の候補が消え、同じ記事をもう一度
  判定できるようになる（プロンプトを直したあとの再判定にも使う）。
  **承認・却下した候補が付いている記事は消せない**（何を承認したのかが分からなくなるため）
- 過去の特別塗装の記事は Aviation Wire のサイト内検索（「特別塗装」「スペシャルマーキング」など）で探せる

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
- Gemini API（特別塗装ニュースの判定。`generateContent` を 1 つ叩くだけなので SDK は入れず `HttpClient` で直接叩く）
- Gmail API（航空局 Excel の取込元。使うのは 3 つのエンドポイントだけなので SDK は入れず `HttpClient` で直接叩く）
- AutoMapper、Crc32.NET、BuildBundlerMinifier、WebEssentials.AspNetCore.PWA
- Bootstrap 3 は CSS だけ使う。JS（modal / collapse / dropdown）は `wwwroot/js/jf-ui.js` で置き換えてあり、
  マークアップと `data-toggle` / `data-dismiss` はそのまま動く。スクリプトから開け閉めするときは `JfUi.modal.show(...)` / `hide(...)`、
  開いたあとの処理は `jf:modal-shown` などのイベントで受ける（`site.min.js` に `site.js` と一緒にまとめている）
- 画面のスクリプトは jQuery を使わずに書く。共通の小物は `site.js` にある
  - 通信は `JfHttp.get(url, params)` / `JfHttp.post(url, data)`（fetch。フォーム形式で送り、JSON なら JSON で返す。
    失敗時は `status` / `statusText` を持つオブジェクトで reject するので、エラー表示はこれまでと同じ形で書ける）
  - `JfDom.ready` / `show` / `hide` / `toggle` / `trigger`。値を書き換えたあと change を飛ばすときは
    `JfDom.trigger(el, 'change')`（ネイティブのイベントなので、どの書き方の処理にも届く）
- jQuery は読み込んでいない（jQuery UI・magnific-popup・Bootstrap の JS・bootstrap-multiselect も外した）
  - 機体詳細のポップアップは `site.js` の `openPopUp` が `<dialog>` と iframe で開く（magnific-popup は使っていない）
  - 管理画面・更新履歴のレジ補完は `site.js` の `attachRegList` が付ける `<datalist>`（jQuery UI は使っていない）
- 表はすべて自前の部品（`fleet-list.js` など）で描いており、DataTables・dayjs・clipboard.js は使っていない
- SheetJS（一覧・非稼働情報の Excel 出力。押したときだけ cdnjs から読み込む）
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
│   ├── JcabImport/   航空局 Excel の解除・解析・プレビュー生成とメール本体の読み取り
│   ├── Gmail/        Gmail API の最小クライアント（検索・1 通取得・添付取得）
│   ├── Livery/       特別塗装ニュースの取得・判定・承認（RSS / Gemini / 反映）
│   ├── AccessStatsService.cs   access_log の集計（/AccessStats）
│   ├── AircraftStore.cs        機体の登録・更新（単票編集と一括取込の共通処理）
│   ├── SearchConditionStore.cs 名前付き検索条件の登録
│   ├── TypeStore.cs            型式のその場登録
│   ├── TypeDetailStore.cs      詳細型式のその場登録
│   ├── SeatConfigStore.cs      コンフィグ（座席仕様）の登録・更新・削除
│   ├── AircraftHistoryStore.cs 更新履歴の修正・削除
│   ├── AircraftReservationStore.cs 機体更新の予約（差分の保存・反映・取消）
│   └── MasterManager.cs        マスタのメモリキャッシュ
├── Adsb/             adsb.lol の履歴ダンプ（GitHub）と ADSBdb の読み取り
├── StandingData/     VRS standing-data（路線・空港）の読み取り
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

有効・無効の切り替えと cron 式の変更は `/Scheduler`（管理コンソールの「設定」→「定期実行ジョブ」）から行います。保存すると DB を書き換えたうえで
動いているスケジューラへその場で反映するため、再起動は要りません。画面には `scheduler_def` の行だけでなく
アプリが持つ `IJob` の実装がすべて並ぶので、定義が無いジョブもここから有効にできます
（`JobCatalog` がクラスを集め、`[Description]` の文言を説明として出します）。

画面で無効にしても、そのとき実行中の処理は止まりません。Quartz は動いているジョブを中断できないため、
最後まで走り切ってから次回以降が実行されなくなります。

cron 式は Quartz の書式で、Unix の cron と違い **秒から始まる 6〜7 項目**です（例: 毎日 6 時は `0 0 6 * * ?`）。
保存時に `CronExpression.IsValidExpression` で検査し、通らない式は登録しません。

| ジョブ | 内容 |
| --- | --- |
| `RefreshWorkingStatusAndPhotoJob` | 退役以外の全機体について Flightradar24 を巡回し、稼働状況と写真を更新。変化を稼働チェックログと Slack 通知用テキストとして記録。**取得元が FR24 のときだけ動く** |
| `RefreshRetiredAircraftPhotoJob` | 退役・抹消済み機体の写真のみ更新 |
| `NotifyWorkingStatusJob` | その日の稼働状況の変化を Slack へ投稿。更新処理が実行中なら完了を待ってから投稿し、未実行なら警告する。**取得元が FR24 のときだけ動く**（adsb.lol のときは `RefreshAdsbFlightJob` が投稿する） |
| `CheckSpecialLiveryNewsJob` | Aviation Wire の RSS を取得し、特別塗装機のニュースから機体更新の候補を作る（[特別塗装ニュースの承認](#特別塗装ニュースの承認)） |
| `ApplyAircraftReservationJob` | 反映日が今日以前の機体更新の予約を反映し、Slack へ知らせる（[機体更新の予約](#機体更新の予約)）。DDL で毎日 0:05 に登録される |
| `RefreshAircraftHexJob` | adsb.lol の履歴ダンプで機体の hex（ICAO 24bit アドレス）を覚え、残りを ADSBdb で引いて `aircraft_hex` に入れる。結果を Slack へ（[機体の hex](#機体の-hexaircraft_hex)） |
| `RefreshFlightRouteJob` | VRS standing-data で路線（callsign→空港）と空港の一覧を丸ごと入れ替え、結果を Slack へ。週1回で足りる（[路線と空港](#路線と空港flight_routeairport)） |
| `RefreshAdsbFlightJob` | adsb.lol の履歴ダンプの前日（UTC）分から日本の機体のフライトを切り出して `adsb_flight` に入れ、取得元が adsb.lol なら稼働状況を更新して Slack へ投稿する。ダンプがまだ無ければ 17:00 まで30分おきに待つ（[稼働状況の取得元](#稼働状況の取得元)・[ダンプからのフライト](#ダンプからのフライトadsb_flight)） |

Flightradar24 を巡回する処理（`RefreshWorkingStatusAndPhotoJob`・`RefreshRetiredAircraftPhotoJob` と、管理コンソールからの手動実行）は、**最初の1機で 403 が返ったら
何も保存せずに中止し、Slack へ通知します**。FR24 は Cloudflare のボット対策でサーバーからのリクエストに
403 を返すことがあり、そのまま続けると全機体を「運航なし」「写真なし」で上書きしてしまうためです
（DB への保存は最後にまとめて行うので、途中で抜ければ何も変わりません）。中止した日は稼働チェックログが
作られないので、`NotifyWorkingStatusJob` は「未実行」の警告を出します。

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
| `GMAIL_CLIENT_ID` / `GMAIL_CLIENT_SECRET` / `GMAIL_REFRESH_TOKEN` | 航空局 Excel を Gmail から取り込む（`/JcabImport`）。3 つ揃ったときだけ Gmail の枠が使える。スコープは `gmail.readonly`。取得方法は[Gmail API の設定](#gmail-api-の設定) |
| `GEMINI_API_KEY` | 特別塗装ニュースの判定（`/LiveryNews`）。未設定でも起動でき、その場合は判定ができないだけ |
| `CF_ACCESS_DEV_ADMIN` | 開発専用。`ASPNETCORE_ENVIRONMENT=Development` かつ `1` のときだけ、認証済みの管理者になりすます（手元では Access の JWT が手に入らないため） |

起動時に、開発専用のものを除く各変数が読めたかどうかを `変数名:Loaded` / `変数名:NotSet` で
ログに出します（値そのものは出しません）。本番は `pass` から取るため、`gpg-agent` の解錠に
失敗していると**無言で空のまま起動**してしまいます。そこに気付けるようにするためのもので、
`journalctl -u ja-fleet` で確認できます。変数を増やしたら `Program.cs` の
`requiredEnvVars` にも足してください。

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

### 登録年月日の date 化と抹消年月日（`alter_aircraft_register_date.sql`）

`aircraft` / `aircraft_history` の `register_date` を `char(10)` の文字列から `date` に変え、
`deregister_date`（抹消年月日、`date`）を足します。ビュー `aircraft_view` / `aircraft_history_view` も作り直します。
**月まで・年までの登録はやめました**（精度の列は持たない）。

| 元の値 | 移行後 |
| --- | --- |
| `yyyy/MM/dd` | その日 |
| `yyyy/MM/xx`・`yyyy/MM/--` | その月の末日 |
| `yyyy/--/--` | その年の 12/31 |
| 空・空白だけ | NULL |
| それ以外 | エラーで止まり、何も変わらない |

- 備考が**ちょうど「yyyy/MM/dd抹消」**の行だけ、抹消年月日へ移して備考を空にする。
  「…→抹消→再登録」の経歴・「一時抹消」は備考のまま残す
- いつもの形でなかったものはスクリプトの末尾で個別に直す（備考が想定どおりのときだけ）

  | レジ | 元の備考 | 移行後 |
  | --- | --- | --- |
  | JA803B | `2024/09/13抹消、元N375PH` | 抹消年月日 2024/09/13、備考「元N375PH」 |
  | JA09MC | `2025/04/031抹消`（誤記） | 抹消年月日 2025/04/03（4/31 は存在しないため）、備考は空 |
  | JA703J | `2023/12/15` | 抹消年月日 2023/12/15、備考は空 |
  | JA8324, JA8578 | なし | 退役（抹消）だが記録が無いので、抹消年月日は空のまま |
- 全体を 1 トランザクションで流し、最後に検算する（行数・登録年月日が消えていない・日まであった値が変わっていない・
  移した抹消年月日が元の備考と一致・それ以外の備考が変わっていない）。合わなければ例外で全部取り消される
- 流す前に `aircraft_bak_regdate` / `aircraft_history_bak_regdate` へ丸ごと退避する。
  `alter_aircraft_register_date_rollback.sql` で戻せる。移行のあと触っていない行は退避の値をそのまま戻し
  （機体は `actual_update_time` が退避と同じ行）、移行のあと更新・追加した行は、日付を文字列に、抹消年月日を
  「yyyy/MM/dd抹消」として備考に戻す
- 開発DBで「移行 → 戻す → 移行」を流し、戻したあとの全行が移行前と一致すること、移行のあとに更新した行が
  組み立て直されることを確かめてある

**手順**: JAFleet と JAFleet.Line を止める → 本番で移行前の集計を取り、例外の行を確かめる → このスクリプトを流す →
新しい `JAFleet.Commons` でビルドした JAFleet と JAFleet.Line を起動する。古いアプリは `date` の列を読めないので、
DB と両方のアプリを同時に切り替える。

`alter_access_log_add_visitor_id.sql`（アクセス集計用の `visitor_id` / `route_key` 列と
期間検索用インデックス）は**デプロイ前**に実行してください。列が無いまま新しいアプリが動くと
アクセスログの INSERT が落ち、記録が止まります（書き込みはバックグラウンドのため、
画面にはエラーが出ません）。

`alter_jcab_import_session_add_gmail_message_id.sql`（Gmail 取込の `gmail_message_id` 列）も
**デプロイ前**に実行してください。列が無いと `/JcabImport` が開けません。

`create_livery_news.sql`（特別塗装ニュースの `livery_news` / `livery_news_candidate`）も
**デプロイ前**に実行してください。テーブルが無いと `/LiveryNews` が開けず、
`CheckSpecialLiveryNewsJob` も動きません。

`create_aircraft_reservation.sql`（機体更新の予約の `aircraft_reservation`、
`livery_news_candidate.start_date` 列、反映ジョブの `scheduler_def` 行）も**デプロイ前**に実行してください。
列が無いと `/LiveryNews` が開けず、テーブルが無いと単票編集が開けません。
`scheduler_def` の行は起動時に読まれるので、デプロイ（再起動）で反映ジョブが載ります。

`create_aircraft_hex.sql`（機体の hex の `aircraft_hex`）は**デプロイ前**に実行してください。テーブルが無いと
`RefreshAircraftHexJob` が落ちます（ほかの画面は使わないので影響しません）。

`create_flight_route_airport.sql`（路線と空港の `flight_route`・`airport`）も**デプロイ前**に実行してください。
テーブルが無いと `RefreshFlightRouteJob` が落ちます（ほかの画面は使わないので影響しません）。

`create_adsb_flight.sql`（ダンプからのフライトの `adsb_flight`）も**デプロイ前**に実行してください。
テーブルが無いと `RefreshAdsbFlightJob` が落ちます（ほかの画面は使わないので影響しません）。

`create_site_setting.sql`（サイト設定の `site_setting`）は、デプロイの前後どちらでも構いません。
テーブルが無いあいだは既定値（非稼働情報のメニューは表示）で動き、`/SiteSetting` で保存しようと
したときだけエラーになります。全ページのレイアウトが読む値なので、起動時とマスタ再読込でメモリへ
読み込み、毎リクエストでは DB に行きません。

### 機体の hex（`aircraft_hex`）

Flightradar24 から取れなくなったので（[Flightradar24 からの取得](#flightradar24-からの取得)）、稼働情報は
adsb.lol の履歴ダンプへ移す予定です。ダンプは機体ごとの軌跡が hex（ICAO 24bit アドレス）単位で入っているので、
まず機体記号と hex の対応を `aircraft_hex` に持たせます（稼働判定の切り替えはこのあと）。

- **元データ**: GitHub の `adsblol/globe_history_YYYY` のリリース `vYYYY.MM.DD-planes-readsb-prod-0`
  （UTC の1日分、翌日の 12:30（日本時間）ごろ公開、`.tar.aa`・`.tar.ab` で約3GiB）。ライセンスは ODbL 1.0 で、
  使った画面には出典（adsb.lol）の表示が要る。**adsb.lol のサーバー（地図・API）は自動で叩かない**
  （地図のホストを自動で叩くと回線ごとブロックされる。自動のクライアントはキー付き API の決まり）
- **読み方**（`Adsb/AdsbDump.cs`）: 分割 tar を保存せずにつないで流し、`traces/xx/trace_full_<hex>.json` のうち
  日本の範囲（840000〜87FFFF）だけを取り出す。中身は gzip の JSON で、`r` がレジ
- **覚え方**（`Batch/RefreshAircraftHex.cs`）: レジ付きで出てきた JAFleet の機体の hex を入れ、`last_seen_date` を進める。
  hex が変わっていたら書き換えて Slack に名前を出す。同じ日に別々の hex で出たレジは見送る
- **ADSBdb**: ダンプに出てこなかった機体（退役と予約登録を除く）だけ、`https://api.adsbdb.com/v0/aircraft/<レジ>` で1機ずつ
  （2秒おき）引く。レジが一致して日本の範囲の hex だけ使う。エラーが返ったら残りは次回に回す。
  ADSBdb の路線データは転載・DB 化が禁止なので使わない
- `RefreshAircraftHexJob` は前日（UTC）分のダンプを読むので、13:00 以降に動かす。`scheduler_def` の行は無いので、
  `/Scheduler` から有効にする（例 `0 0 13 * * ?`）
- 開発DBで 2026-11-22 のダンプを流した結果（17分46秒）: 日本の hex 753 件のうち JAFleet の 571 機をダンプで覚え、
  ADSBdb で 75 機を追加。ADSBdb でも見つからなかった 200 機は、ほとんどが予約登録だけのレジ
  （JA030A〜、JA351J〜、JA731B〜 など）だったので、2回目からは予約登録を引かないようにした

### 路線と空港（`flight_route`・`airport`）

軌跡の callsign から出発地・到着地を出すための表です（使うのは稼働判定を切り替えてから）。

- **元データ**: VRS standing-data（https://github.com/vradarserver/standing-data 、CC0）。リポジトリの tarball
  （約6MB）を1回取って流し、`airports/schema-01/*/*.csv` と `routes/schema-01/*/<航空会社>-all.csv` を拾う
  （`StandingData/VrsStandingData.cs`）。CC0 なので出典の表示は要らない
- **路線**: JA 機が使う callsign の航空会社（`JAPANESE_CALLSIGN_AIRLINES` の25社。春秋航空日本の `SJO` はあとから足した）だけ。`airport_codes` は ICAO を
  飛ぶ順に `-` でつないだもの（経由便は3つ以上）。JAC・NTH（HAC）・CUK は VRS に無い。HAC の `JAL2761` なども
  JAL のファイルに無かったので、こうした便は軌跡の端に一番近い空港で代わりにする想定
- **空港**: 全世界（約3.4万件、ヘリポートも含む）。都市名は英語で、FR24 と違うことがある（小松は Kanazawa）。
  整備拠点（OKA・TPE・TNN・XMN・HKG・MNL・SIN・XSP・QPG・TNA）はすべて入っている
- **入れ替え方**: 1トランザクションで全部消して入れ直す。空港が2万件未満・路線が3千件未満なら、配布物が
  おかしいとみて入れ替えずに止める
- VRS の航空会社の表（`airlines.csv`）は日本の会社に誤りがある（`RAC` が Icar Air など）ので使わない
- `scheduler_def` の行は無いので、`/Scheduler` から有効にする（例 毎週月曜 3:00 は `0 0 3 ? * MON`）
- 開発DBで流した結果: 路線 6,074 件（`SJO` を足す前。足したあとは 6,176 件。ANA 2,096、JAL 1,810、APJ 519、SKY 340…）、空港 34,128 件。
  路線に出てくる空港コードはすべて空港の表にあった。所要は10秒ほど

### 稼働状況の取得元

Flightradar24（FR24）がサーバーからの自動アクセスを止めた（Cloudflare の 403。規約でも API 以外の自動アクセスは禁止）ので、
稼働状況は adsb.lol の履歴ダンプから作るように切り替えました。FR24 が使えるようになったときのために、FR24 の巡回は
消さずに残してあり、`/SiteSetting` の「稼働状況の取得元」で戻せます（`site_setting` の `working_status_source`。
行が無ければ adsb.lol）。保存すればその場で効き、再起動は要りません。

| | adsb.lol（既定） | FR24 |
| --- | --- | --- |
| 稼働状況と稼働チェックログを作る | `RefreshAdsbFlightJob`（毎日 13:00 の想定） | `RefreshWorkingStatusAndPhotoJob`（毎朝） |
| Slack の稼働通知 | `RefreshAdsbFlightJob` が更新のすぐあと | `NotifyWorkingStatusJob`（毎朝） |
| もう一方のジョブ | FR24 の2つは何もせずに終わる | `RefreshAdsbFlightJob` は `adsb_flight` の記録だけ続ける |
| 管理コンソールの「ステータス・写真更新」 | 前日（UTC）分のダンプを取り込み、稼働状況を更新して通知 | FR24 を巡回（いままでどおり） |
| 写真 | 機体詳細は FR24 の機体ページを埋め込む（DB の写真は使わない） | FR24 の巡回のついでに DB の写真も更新 |

FR24 の巡回で使っているのは master の古い HTML の読み方なので、FR24 を戻すときは `fr24-inertia-json` ブランチ
（いまの FR24 の画面の JSON を読む修正）を取り込んでから切り替えてください。

**adsb.lol での決め方**（`Batch/AdsbWorkingStatus.cs`）。区分・稼働チェックログ・通知の形は FR24 のときと同じです。

- **最後に飛んだ便**: データ日に飛んだ便のうち一番遅いものの出発時刻・出発地・到着地・便名・状態を `working_status` に
  入れる（流し直しで古い日を入れたときは、新しい便を上書きしない）。飛んでいなければ前の値のまま
- **稼働**: 最後に飛んだ日が、データの締め（翌日 9:00）の日付から2日以内なら稼働（FR24 のときと同じ日数）。
  ダンプが1日遅れなので、FR24 のときより変化に気づくのが1日ほど遅い
- **整備入り**: 稼働から非稼働に落ちたとき、最後の便の到着地が整備拠点（`MAINTE_PLACE` の10空港）なら整備入り。
  到着地が決まらなかった便は、最後の位置が拠点から150km以内・10,000ft 以下なら整備入りとみる
- **整備終了**: 整備中の機体がまた稼働になったとき
- **テストレジ**: JA 登録前の外国レジ（`aircraft.test_registration`）の hex を ADSBdb で引いて `aircraft_hex` に入れ、
  ダンプから日本の範囲の外でもその hex の軌跡を取り出す。飛んでいたら「テストレジが稼働」に出す（稼働にはしない）
- **ダンプが遅れたとき**: 17:00 まで30分おきに待ち、それでも無ければ Slack に知らせて稼働状況は書き換えない
- 通知には「データ adsb.lol 2026-11-23 UTC」のようにどの日のダンプかを出す
- **実行中のログ**: `RefreshAdsbFlightJob` は、1回分のログを全部 `【RefreshAdsbFlight:<ID>】` の名前で標準出力に出す
  （サーバでは `journalctl` で見る）。開始 → ダンプの有無（無ければ次に確かめる時刻）→ 取得開始（分割数・合計 GiB）→
  **取得中は30秒ごとに「取得中 1.50 / 3.06 GiB（49%）、分割 1/2、日本の機体 312 件、経過 2分30秒」** → 取得完了（便数）→
  保存 → 稼働状況の更新（区分ごとの件数）→ Slack に投稿して終了（全体の時間）。`RefreshAircraftHexJob` も取得中の進み具合と、
  ADSBdb を25機引くごとの進み具合を出す
- 非稼働情報（`/NotWorkingInfo`）と稼働チェックログの画面には、ODbL の出典表示（Data: adsb.lol (ODbL 1.0)）を出す
- 切り替えた日の最初の通知は、FR24 が壊れていたあいだ非稼働に落ちていた機体がまとめて「運用中非稼働が稼働」に出る

### ダンプからのフライト（`adsb_flight`）

adsb.lol の履歴ダンプ1日（UTC。日本時間ではその日の 9:00〜翌 9:00）分の軌跡を、1機・1本ずつのフライトにした記録です。
取得元が adsb.lol のときは、稼働状況（`working_status`）をこの記録から作ります（[稼働状況の取得元](#稼働状況の取得元)）。
同じ日を流し直すと、その日の行は入れ替わります。ダンプのレジで `aircraft_hex` も覚えます。

- **機体の当て方**: 軌跡のレジ（`r`）が JAFleet にあればそれ。レジが無い・合わないときは `aircraft_hex` の hex で当てる
- **フライトの区切り**（`Adsb/FlightLegSplitter.cs`）: readsb が付ける「新しい区間」のフラグ、地上から空中に出た所、
  callsign が別の便に変わった所、低い高度（10,000ft 以下）のまま30分以上途切れた所。空中の点が3つ未満の区間と、
  地上の点しか無い区間は捨てる。callsign は空中で一番多く出たもの（`@@@@@@@@` のような受信のゴミは無視）
- **便名**（`Adsb/CallsignConverter.cs`）: callsign の会社3文字を FR24 と同じ2文字にする（`JAL201`→`JL201`、
  ANA ウイングスの `AKX`→`EH`）。トキエアは FR24 では `CG` だったが、正しい `BV` にする。表に無い会社やレジの
  callsign はそのまま。JAC のように IATA の形（`JL3724`）で出す機体は、路線を引くときだけ `JAL3724` にする
- **出発地・到着地**（`Adsb/AirportResolver.cs`）:
  1. callsign の路線（`flight_route`）の空港が軌跡の端の近くにあればそれ（`route`）。地上・3,000ft 以下の端なら30km、
     10,000ft 以下なら150km以内。経由便は、隣り合う2空港の組で両端に一番よく合うものを選ぶ
  2. 端が巡航中（10,000ft より上）で切れていて確かめられなければ、路線どおり（`route_unverified`）
  3. 路線が無い・合わないときは、端が低ければ一番近い空港（`nearest`。IATA のある空港だけ、地上なら15km・空中なら40km以内）
  4. それでもだめなら決めない（`none`）

  空中で切れた端は、出発側なら降下中でないこと、到着側なら上昇中でないことも確かめる（空港の近くを降りていく
  途中から受信が始まった便を、その空港から出たことにしないため）
- **状態**: 着陸まで追えたら `Landed HH:MM`（日本時間）。接地まで受信できない空港が多いので、到着地を `route`・`nearest` で
  決められたものも着陸とみる。それ以外は `Last seen HH:MM`
- 表示は FR24 と同じ「都市名 (IATA)」。`DepartureTime` は最初に空中にいた時刻なので、FR24 の出発時刻（予定）とは
  数十分ずれる
- **FR24 との突き合わせ**（開発DB、2026-11-18・19 の UTC 分）: FR24 が壊れる直前に記録していた各機の最後の便
  558 便のうち、同じ機体・同じ便名の便がダンプにあったのは 504 便。そのうち出発地は 494（98.0%）、到着地は 496（98.4%）が
  一致した。外れの多くは巡航中に受信が切れて決められなかった便（`none`）。便名が合わなかった 45 便は、その便がダンプに
  映っていなかったもの・callsign が空のもの・ANA ウイングス（そのあと `EH` に直した）・トキエア（`BV`）
- 1日分（約800機・約3,000便）の解析と保存は10秒ほど。時間のほとんどはダンプ（約3GiB）の取得
- `scheduler_def` の行は無いので、`/Scheduler` から有効にする（例 毎日 13:00 は `0 0 13 * * ?`）

主なテーブル・ビューは `JAFleet.Commons` の `JAFleetContext` を参照してください
（`aircraft`、`aircraft_view`、`aircraft_history`、`aircraft_photo`、`working_status`、
`airline`、`type`、`type_detail`、`code`、`search_condition`、`access_log`、`log`、
`scheduler_def`、`jcab_import_session`、`livery_news`、`livery_news_candidate`、`site_setting`、`aircraft_reservation` など）。
