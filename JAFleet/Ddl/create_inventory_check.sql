-- 管理者の棚卸（/Inventory）で「内容を確認した」目印を付けた機体
-- registration_number : 機体記号（aircraft.registration_number）。1 機 1 行
-- checked_at          : 目印を付けた日時。これより後に機体が更新されていれば、画面は「要再確認」として出す
-- 目印を外すと行ごと消す。機体を消しても行は残るが、画面は aircraft_view と突き合わせるので出てこない。
-- テーブルが無いと /Inventory が開けない（ほかの画面は使わない）。
CREATE TABLE inventory_check (
    registration_number varchar(10) PRIMARY KEY,
    checked_at          timestamp   NOT NULL
);
