-- アクセス集計（/AccessStats）用のaccess_log拡張
-- visitor_id: 訪問者を見分けるための署名付きCookieから取り出した値。
--             アプリ（VisitorIdIssuer）がランダムな16桁を配るだけで、個人を特定する情報は入らない。
-- route_key : MVCのルートから取れた "コントローラー名/アクション名"。
--             静的ファイルやどのルートにも当たらなかったリクエストではNULLになるので、
--             集計で「普通のページへのアクセスか」を見分けるのに使う。
--             既存の行はすべてNULLになる＝集計対象外になるため、デプロイ直後の画面は
--             過去分が空に見える。新しい記録がたまるにつれて埋まっていく。
-- インデックス: 集計は必ずrequest_timeの範囲で絞るため、その範囲検索用。
--               access_logは毎晩31日より古い行を消している（log_process.sh）ので、
--               インデックスが肥大し続けることはない。
-- 列が無い状態で新しいアプリが動くとINSERTが落ちるため、デプロイ前に実行すること。
ALTER TABLE jafleet.access_log ADD COLUMN IF NOT EXISTS visitor_id varchar(64);
ALTER TABLE jafleet.access_log ADD COLUMN IF NOT EXISTS route_key varchar(128);

CREATE INDEX IF NOT EXISTS idx_access_log_request_time ON jafleet.access_log (request_time);
