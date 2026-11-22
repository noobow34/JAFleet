-- alter_aircraft_register_date.sql を取り消す（register_date を文字列に戻し、deregister_date を備考へ戻す）。
--   移行のあと変わっていない行: 退避（*_bak_regdate）の登録年月日と備考をそのまま戻す
--     （機体は actual_update_time が退避と同じ行。保存すると必ず進むので、触っていないと分かる。
--       履歴は登録年月日・抹消年月日・備考が、移行で作った値のままの行）
--   移行のあと更新・追加した行: いまの値から組み立てる
--     登録年月日 … 退避の元の文字列が同じ日付を表していれば元の文字列、違えば yyyy/MM/dd
--     抹消年月日 … 「yyyy/MM/dd抹消」として備考の先頭に戻す（備考が残っていれば「、」でつなぐ）
-- 流す前に: アプリを止め、古い JAFleet.Commons でビルドしたものに戻してから起動する。

BEGIN;

CREATE FUNCTION pg_temp.to_register_date(s text) RETURNS date
LANGUAGE plpgsql IMMUTABLE AS $$
DECLARE
    t text := btrim(coalesce(s, ''));
    m text[];
BEGIN
    IF t = '' THEN
        RETURN NULL;
    END IF;
    m := regexp_match(t, '^(\d{4})/(\d{1,2})/(\d{1,2})$');
    IF m IS NOT NULL THEN
        RETURN make_date(m[1]::int, m[2]::int, m[3]::int);
    END IF;
    m := regexp_match(t, '^(\d{4})/(\d{1,2})(?:/(?:xx|XX|--))?$');
    IF m IS NOT NULL THEN
        RETURN (make_date(m[1]::int, m[2]::int, 1) + interval '1 month - 1 day')::date;
    END IF;
    m := regexp_match(t, '^(\d{4})(?:/(?:xx|XX|--)/(?:xx|XX|--))?$');
    IF m IS NOT NULL THEN
        RETURN make_date(m[1]::int, 12, 31);
    END IF;
    RETURN NULL;
END
$$;

DROP VIEW aircraft_view;
DROP VIEW aircraft_history_view;

ALTER TABLE aircraft ADD COLUMN register_date_old character(10);
ALTER TABLE aircraft_history ADD COLUMN register_date_old character(10);
ALTER TABLE aircraft ADD COLUMN restored boolean NOT NULL DEFAULT false;
ALTER TABLE aircraft_history ADD COLUMN restored boolean NOT NULL DEFAULT false;

-- ---- 移行のあと変わっていない行は退避の値をそのまま戻す ----
UPDATE aircraft a
   SET register_date_old = b.register_date, remarks = b.remarks, restored = true
  FROM aircraft_bak_regdate b
 WHERE b.registration_number = a.registration_number
   AND a.actual_update_time IS NOT DISTINCT FROM b.actual_update_time;

UPDATE aircraft_history a
   SET register_date_old = b.register_date, remarks = b.remarks, restored = true
  FROM aircraft_history_bak_regdate b
 WHERE b.ah_id = a.ah_id
   AND pg_temp.to_register_date(b.register_date) IS NOT DISTINCT FROM a.register_date
   AND CASE WHEN b.remarks ~ '^\s*\d{4}/\d{2}/\d{2}\s*抹消\s*$'
            THEN a.remarks IS NULL
                 AND a.deregister_date IS NOT DISTINCT FROM pg_temp.to_register_date(substring(b.remarks FROM '\d{4}/\d{2}/\d{2}'))
            ELSE a.deregister_date IS NULL AND a.remarks IS NOT DISTINCT FROM b.remarks
       END;

-- ---- 残り（移行のあと更新・追加した行）はいまの値から組み立てる ----
UPDATE aircraft SET register_date_old = to_char(register_date, 'YYYY/MM/DD') WHERE NOT restored;
UPDATE aircraft_history SET register_date_old = to_char(register_date, 'YYYY/MM/DD') WHERE NOT restored;

UPDATE aircraft a SET register_date_old = b.register_date
  FROM aircraft_bak_regdate b
 WHERE NOT a.restored AND b.registration_number = a.registration_number
   AND pg_temp.to_register_date(b.register_date) IS NOT DISTINCT FROM a.register_date;
UPDATE aircraft_history a SET register_date_old = b.register_date
  FROM aircraft_history_bak_regdate b
 WHERE NOT a.restored AND b.ah_id = a.ah_id
   AND pg_temp.to_register_date(b.register_date) IS NOT DISTINCT FROM a.register_date;

UPDATE aircraft
   SET remarks = to_char(deregister_date, 'YYYY/MM/DD') || '抹消' || coalesce('、' || nullif(btrim(remarks), ''), '')
 WHERE NOT restored AND deregister_date IS NOT NULL;
UPDATE aircraft_history
   SET remarks = to_char(deregister_date, 'YYYY/MM/DD') || '抹消' || coalesce('、' || nullif(btrim(remarks), ''), '')
 WHERE NOT restored AND deregister_date IS NOT NULL;

ALTER TABLE aircraft DROP COLUMN restored;
ALTER TABLE aircraft_history DROP COLUMN restored;
ALTER TABLE aircraft DROP COLUMN register_date;
ALTER TABLE aircraft RENAME COLUMN register_date_old TO register_date;
ALTER TABLE aircraft_history DROP COLUMN register_date;
ALTER TABLE aircraft_history RENAME COLUMN register_date_old TO register_date;
ALTER TABLE aircraft DROP COLUMN deregister_date;
ALTER TABLE aircraft_history DROP COLUMN deregister_date;

-- ---- ビューを元の形で作り直す ----
CREATE VIEW aircraft_view AS
 SELECT concat(al.display_order, t.display_order, a.registration_number) AS display_order,
    al.airline_group_code,
    a.airline,
    al.airline_name_jp_short,
    td.type_code,
    t.type_name,
    td.type_detail_code,
    a.type_detail_id,
    td.type_detail_name,
    a.registration_number,
    a.serial_number,
    a.register_date,
    a.wifi_code,
    c1.value AS wifi,
    c1.value_short AS wifi_short,
    a.operation_code,
    c2.value AS operation,
    a.remarks,
    a.creation_time,
    a.update_time,
    a.link_url,
    a.actual_update_time,
    a.special_livery,
    a.test_registration,
    a.maintenance_notify,
    sc.f,
    sc.c,
    sc.py,
    sc.p,
    sc.j,
    sc.y,
    sc.url AS seat_url,
    ap.photo_url,
    ap.photo_direct_large,
    ap.photo_direct_small
   FROM aircraft a
     LEFT JOIN airline al ON a.airline::text = al.airline_code::text
     LEFT JOIN code c1 ON a.wifi_code = c1.key::bpchar AND c1.code_type::text = 'WIFI'::text
     LEFT JOIN code c2 ON a.operation_code = c2.key::bpchar AND c2.code_type::text = 'OPE'::text
     LEFT JOIN type_detail td ON a.type_detail_id = td.type_detail_id
     LEFT JOIN type t ON td.type_code::text = t.type_code::text
     LEFT JOIN seat_configuration sc ON a.seat_config = sc.config_id
     LEFT JOIN aircraft_photo ap ON a.registration_number = ap.registration_number;

CREATE VIEW aircraft_history_view AS
 SELECT a.ah_id,
    a.seq,
    concat(al.display_order, t.display_order, a.registration_number) AS display_order,
    al.airline_group_code,
    a.airline,
    al.airline_name_jp_short,
    td.type_code,
    t.type_name,
    td.type_detail_code,
    a.type_detail_id,
    td.type_detail_name,
    a.registration_number,
    a.serial_number,
    a.register_date,
    a.wifi_code,
    c1.value AS wifi,
    c1.value_short AS wifi_short,
    a.operation_code,
    c2.value AS operation,
    a.remarks,
    a.creation_time,
    a.update_time,
    a.link_url,
    a.actual_update_time,
    a.special_livery,
    a.test_registration,
    a.maintenance_notify,
    sc.f,
    sc.c,
    sc.py,
    sc.p,
    sc.j,
    sc.y,
    sc.url AS seat_url
   FROM aircraft_history a
     LEFT JOIN airline al ON a.airline::text = al.airline_code::text
     LEFT JOIN code c1 ON a.wifi_code = c1.key::bpchar AND c1.code_type::text = 'WIFI'::text
     LEFT JOIN code c2 ON a.operation_code = c2.key::bpchar AND c2.code_type::text = 'OPE'::text
     LEFT JOIN type_detail td ON a.type_detail_id = td.type_detail_id
     LEFT JOIN type t ON td.type_code::text = t.type_code::text
     LEFT JOIN seat_configuration sc ON a.seat_config = sc.config_id;

COMMIT;

-- 戻し終えて問題が無ければ、退避テーブルは消してよい
-- DROP TABLE aircraft_bak_regdate;
-- DROP TABLE aircraft_history_bak_regdate;
