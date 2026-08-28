-- messageテーブルの返信先カラムの綴り修正
-- Replyのつもりでreplay_toとしていたものをreply_toに直す。
-- アプリ側のMessage.ReplyToが[Column("reply_to")]を見るようになるため、
-- 新しいバイナリに切り替える直前に実行する。実行から再起動までの間に
-- 問い合わせフォームの送信があるとDBへの記録だけ失敗するが、Slack通知は
-- 通るので内容は失われない。
ALTER TABLE message RENAME COLUMN replay_to TO reply_to;
