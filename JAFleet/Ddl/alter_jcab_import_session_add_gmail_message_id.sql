-- Gmailから直接取り込めるようにしたときの追加。
-- どのメールから作った一時保存かを持たせ、取込画面のメール一覧で
-- 「作業中」を出して再開へ誘導する（アップロードから作った分はNULL）。
-- create_jcab_import_session.sql には最初から入っているので、適用するのは既存DBだけ。
ALTER TABLE jcab_import_session ADD COLUMN gmail_message_id varchar(64);

CREATE INDEX idx_jcab_import_session_gmail_message_id ON jcab_import_session (gmail_message_id);
