-- 航空局Excel取込の一時保存
-- 復号済みのxlsxそのものと、レジごとの編集内容（JSON）を持つ。
-- 再開時はxlsxを解析し直したうえで編集内容を被せるため、
-- 保存後にマスタや機体情報が変わっても最新の状態と突き合わせられる。
CREATE TABLE jcab_import_session (
    session_id     serial       PRIMARY KEY,
    file_name      varchar(255) NOT NULL,
    target_month   varchar(7),
    file_data      bytea        NOT NULL,
    overrides_json text         NOT NULL DEFAULT '{}',
    created_at     timestamp    NOT NULL,
    updated_at     timestamp    NOT NULL
);

CREATE INDEX idx_jcab_import_session_updated_at ON jcab_import_session (updated_at DESC);
