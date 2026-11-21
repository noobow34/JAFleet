-- 管理コンソールの「サイト設定」（/SiteSetting）で切り替える設定
-- key   : 設定の名前（SiteSettings.cs の定数）
-- value : 値。真偽値は 'true' / 'false'
-- 行が無いキーはアプリ側の既定値になる（非稼働情報のメニューは表示）。
-- テーブルが無いままアプリが動いても既定値で動き、保存しようとしたときだけエラーになる。
CREATE TABLE site_setting (
    key        varchar(100)  PRIMARY KEY,
    value      varchar(1000) NOT NULL,
    updated_at timestamp     NOT NULL
);
