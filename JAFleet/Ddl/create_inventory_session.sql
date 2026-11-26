-- 棚卸（/Inventory）を名前付きで複数持てるようにする。目印（inventory_check）は棚卸ごとに持つ。
-- create_inventory_check.sql のあとに流す。
-- inventory_check に行があると session_id を足せずに止まり、何も変わらない（行の無いうちに流すこと）。
BEGIN;

-- 名前を付けて保存した棚卸 1 回ぶん
-- condition_json     : 検索条件（検索画面の保存済み条件 search_condition と同じ形の JSON）
-- last_opened_at     : 最後に開いた日時。画面を開いたとき、未完了のうちこれが新しいものを自動で開く
-- total_count        : 最後に数えたときの、条件に当たる機体の数
-- checked_count      : 最後に数えたときの、確認済（要再確認を除く）の機体の数
-- completed_at       : 完了した日時（未完了は NULL）。全部確認済になると自動で入る。画面から手で付け外しもできる
-- completed_manually : 手で完了にしたとき true
CREATE TABLE inventory_session (
    id                 serial        PRIMARY KEY,
    name               varchar(100)  NOT NULL,
    condition_json     text          NOT NULL,
    created_at         timestamp     NOT NULL,
    last_opened_at     timestamp     NOT NULL,
    total_count        integer       NOT NULL DEFAULT 0,
    checked_count      integer       NOT NULL DEFAULT 0,
    completed_at       timestamp,
    completed_manually boolean       NOT NULL DEFAULT false
);

-- 目印を棚卸ごとにする。棚卸を消すときは、アプリが先に目印を消してから棚卸を消す
ALTER TABLE inventory_check ADD COLUMN session_id integer NOT NULL REFERENCES inventory_session (id);
ALTER TABLE inventory_check DROP CONSTRAINT inventory_check_pkey;
ALTER TABLE inventory_check ADD PRIMARY KEY (session_id, registration_number);

COMMIT;
