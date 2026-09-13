-- app_infoテーブルの削除
-- バージョン表示をビルド時にアセンブリへ埋め込む方式にしたため不要になった。
-- jr.sh / rb.sh がこのテーブルを書き込まない版に置き換わってから実行する
-- （古いjr.shはset -eのためテーブルが無いとデプロイ途中で止まる）。
DROP TABLE IF EXISTS jafleet.app_info;
