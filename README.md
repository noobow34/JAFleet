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
| 単票編集 | `/E/{レジ}` | 機体情報の新規登録・更新。更新時は `aircraft_history` へ退避。登録ボタンでは履歴を残すかどうかを確認する |
| 更新履歴の編集 | `/AircraftHistory?reg={レジ}` | 機体詳細に出る更新履歴（`aircraft_history`）の修正・削除 |
| 同一型式の一括登録 | `/BulkRegister` | 「レジ / 登録年月日 / 製造番号」をタブ・カンマ・空白区切りで貼り付けてまとめて登録 |
| 航空局 Excel 取込 | `/JcabImport` | 国土交通省航空局から届くパスワード付き Excel を、Gmail から直接またはアップロードで取り込む |
| コンフィグマスタ | `/SeatConfig` | 航空会社・型式ごとの座席仕様（コンフィグ）を登録・更新・削除する |
| 特別塗装ニュース | `/LiveryNews` | Aviation Wire の RSS から Gemini が作った特別塗装の更新候補を承認・却下する |
| マスタ再読込 | `/ReloadMaster` | 航空会社・型式などのマスタをメモリへ読み直す |
| 定期実行ジョブ | `/Scheduler` | Quartz のジョブの有効・無効と cron 式を変更する |
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
  ドメイン（`from:mlit.go.jp`）で見て、件名の「航空機登録情報」で絞る。画面の入力欄で一時的に変えられるが、
  保存はしない（既定値を変えるならコードを直す）
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
- Gmail への問い合わせは数秒かかることがあるため、画面の枠（`Index`）とメール一覧
  （部分ビュー `_GmailList`、`GET /JcabImport/GmailList`）に分けてある。アクセス集計と同じ作り
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
- 詳細型式の選択と、コンフィグの選択・その場登録は単票編集と同じ部品を使う
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
   （単票編集と同じ処理を通るので、更新前の内容は `aircraft_history` に残る）

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
│   ├── JcabImport/   航空局 Excel の解除・解析・プレビュー生成とメール本体の読み取り
│   ├── Gmail/        Gmail API の最小クライアント（検索・1 通取得・添付取得）
│   ├── Livery/       特別塗装ニュースの取得・判定・承認（RSS / Gemini / 反映）
│   ├── AccessStatsService.cs   access_log の集計（/AccessStats）
│   ├── AircraftStore.cs        機体の登録・更新（単票編集と一括取込の共通処理）
│   ├── SearchConditionStore.cs 名前付き検索条件の登録
│   ├── TypeDetailStore.cs      詳細型式のその場登録
│   ├── SeatConfigStore.cs      コンフィグ（座席仕様）の登録・更新・削除
│   ├── AircraftHistoryStore.cs 更新履歴の修正・削除
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
| `CheckSpecialLiveryNewsJob` | Aviation Wire の RSS を取得し、特別塗装機のニュースから機体更新の候補を作る（[特別塗装ニュースの承認](#特別塗装ニュースの承認)） |

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

`alter_access_log_add_visitor_id.sql`（アクセス集計用の `visitor_id` / `route_key` 列と
期間検索用インデックス）は**デプロイ前**に実行してください。列が無いまま新しいアプリが動くと
アクセスログの INSERT が落ち、記録が止まります（書き込みはバックグラウンドのため、
画面にはエラーが出ません）。

`alter_jcab_import_session_add_gmail_message_id.sql`（Gmail 取込の `gmail_message_id` 列）も
**デプロイ前**に実行してください。列が無いと `/JcabImport` が開けません。

`create_livery_news.sql`（特別塗装ニュースの `livery_news` / `livery_news_candidate`）も
**デプロイ前**に実行してください。テーブルが無いと `/LiveryNews` が開けず、
`CheckSpecialLiveryNewsJob` も動きません。

主なテーブル・ビューは `JAFleet.Commons` の `JAFleetContext` を参照してください
（`aircraft`、`aircraft_view`、`aircraft_history`、`aircraft_photo`、`working_status`、
`airline`、`type`、`type_detail`、`code`、`search_condition`、`access_log`、`log`、
`scheduler_def`、`jcab_import_session`、`livery_news`、`livery_news_candidate` など）。
