-- 登録年月日（register_date）を文字列から date に変え、抹消年月日（deregister_date）を備考から独立させる。
-- 対象: aircraft / aircraft_history と、それを読むビュー aircraft_view / aircraft_history_view。
-- 戻すときは alter_aircraft_register_date_rollback.sql（このスクリプトが作る退避テーブルから戻す）。
--
-- 変換の決まり（月まで・年までの登録はやめたので末日に寄せる）
--   yyyy/MM/dd            → その日
--   yyyy/MM/xx, yyyy/MM/-- → その月の末日
--   yyyy/--/--            → その年の 12/31
--   空・空白だけ          → NULL
--   それ以外              → エラーで止める（何も書き換えずに終わる）
-- 抹消年月日
--   備考がちょうど「yyyy/MM/dd抹消」の行だけ、抹消年月日へ移して備考を空にする
--   それ以外（「…→抹消→再登録」の経歴、「一時抹消」、ほかの文と混ざったもの）は備考のまま残す。
--   個別に直すものは末尾の「個別の手直し」でやる
--
-- 流す前に: アプリ（JAFleet と JAFleet.Line）を止める。古いアプリは date になった列を読めない。
-- 流したあと: 新しい JAFleet.Commons でビルドした JAFleet と JAFleet.Line を起動する。
-- 全体を 1 トランザクションで流す。途中で失敗したら何も変わらない。

BEGIN;

-- ---- 1. 退避（戻すときに使う。問題が無いと分かったら DROP してよい） ----
CREATE TABLE aircraft_bak_regdate AS TABLE aircraft;
CREATE TABLE aircraft_history_bak_regdate AS TABLE aircraft_history;

-- ---- 2. 列を参照しているビューを消す（列の型を変えるため） ----
DROP VIEW aircraft_view;
DROP VIEW aircraft_history_view;

-- ---- 3. 変換 ----
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
        -- make_date は存在しない日付（2/30 など）でエラーになる。黙って翌月に繰り越させない
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
    RAISE EXCEPTION '登録年月日を日付にできません: "%"', s;
END
$$;

ALTER TABLE aircraft ADD COLUMN register_date_new date;
ALTER TABLE aircraft ADD COLUMN deregister_date date;
ALTER TABLE aircraft_history ADD COLUMN register_date_new date;
ALTER TABLE aircraft_history ADD COLUMN deregister_date date;

UPDATE aircraft SET register_date_new = pg_temp.to_register_date(register_date);
UPDATE aircraft_history SET register_date_new = pg_temp.to_register_date(register_date);

-- 備考がちょうど「yyyy/MM/dd抹消」の行だけ抹消年月日へ移す
UPDATE aircraft
   SET deregister_date = pg_temp.to_register_date(substring(remarks FROM '\d{4}/\d{2}/\d{2}')),
       remarks = NULL
 WHERE remarks ~ '^\s*\d{4}/\d{2}/\d{2}\s*抹消\s*$';
UPDATE aircraft_history
   SET deregister_date = pg_temp.to_register_date(substring(remarks FROM '\d{4}/\d{2}/\d{2}')),
       remarks = NULL
 WHERE remarks ~ '^\s*\d{4}/\d{2}/\d{2}\s*抹消\s*$';

ALTER TABLE aircraft DROP COLUMN register_date;
ALTER TABLE aircraft RENAME COLUMN register_date_new TO register_date;
ALTER TABLE aircraft_history DROP COLUMN register_date;
ALTER TABLE aircraft_history RENAME COLUMN register_date_new TO register_date;

-- ---- 4. 検算（合わなければ例外で止まり、全部取り消される） ----
DO $$
DECLARE
    n int;
BEGIN
    -- 行数が変わっていない
    IF (SELECT count(*) FROM aircraft) <> (SELECT count(*) FROM aircraft_bak_regdate)
       OR (SELECT count(*) FROM aircraft_history) <> (SELECT count(*) FROM aircraft_history_bak_regdate) THEN
        RAISE EXCEPTION '行数が合いません';
    END IF;

    -- 元が空でない登録年月日が NULL になっていない
    SELECT count(*) INTO n FROM aircraft a JOIN aircraft_bak_regdate b USING (registration_number)
     WHERE btrim(coalesce(b.register_date, '')) <> '' AND a.register_date IS NULL;
    IF n > 0 THEN RAISE EXCEPTION 'aircraft: 登録年月日が消えた行が % 件', n; END IF;
    SELECT count(*) INTO n FROM aircraft_history a JOIN aircraft_history_bak_regdate b USING (ah_id)
     WHERE btrim(coalesce(b.register_date, '')) <> '' AND a.register_date IS NULL;
    IF n > 0 THEN RAISE EXCEPTION 'aircraft_history: 登録年月日が消えた行が % 件', n; END IF;

    -- 日まであった登録年月日は、書き戻すと元の文字列と一致する
    SELECT count(*) INTO n FROM aircraft a JOIN aircraft_bak_regdate b USING (registration_number)
     WHERE b.register_date ~ '^\d{4}/\d{2}/\d{2}$' AND to_char(a.register_date, 'YYYY/MM/DD') <> b.register_date;
    IF n > 0 THEN RAISE EXCEPTION 'aircraft: 登録年月日が変わった行が % 件', n; END IF;
    SELECT count(*) INTO n FROM aircraft_history a JOIN aircraft_history_bak_regdate b USING (ah_id)
     WHERE b.register_date ~ '^\d{4}/\d{2}/\d{2}$' AND to_char(a.register_date, 'YYYY/MM/DD') <> b.register_date;
    IF n > 0 THEN RAISE EXCEPTION 'aircraft_history: 登録年月日が変わった行が % 件', n; END IF;

    -- 移した抹消年月日は、元の備考の日付と一致する
    SELECT count(*) INTO n FROM aircraft a JOIN aircraft_bak_regdate b USING (registration_number)
     WHERE a.deregister_date IS NOT NULL
       AND to_char(a.deregister_date, 'YYYY/MM/DD') || '抹消' <> regexp_replace(b.remarks, '\s', '', 'g');
    IF n > 0 THEN RAISE EXCEPTION 'aircraft: 抹消年月日が備考と合わない行が % 件', n; END IF;

    -- 抹消年月日へ移した行以外の備考は変えていない
    SELECT count(*) INTO n FROM aircraft a JOIN aircraft_bak_regdate b USING (registration_number)
     WHERE a.deregister_date IS NULL AND a.remarks IS DISTINCT FROM b.remarks;
    IF n > 0 THEN RAISE EXCEPTION 'aircraft: 備考が変わった行が % 件', n; END IF;
END
$$;

-- ---- 5. ビューを作り直す（register_date の位置に、そのあと deregister_date を足す） ----
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
    a.deregister_date,
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
    a.deregister_date,
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

-- ---- 6. 個別の手直し（開発DBで見つかったもの。本番は事前の集計で差分を確かめてから流す） ----
-- 備考がいつもの形でなかったもの。備考が想定どおりのときだけ直す（違えば何もしない）
-- 抹消年月日とほかの文が混ざっていた: 抹消年月日を移し、残りの文は備考に残す
UPDATE aircraft SET deregister_date = DATE '2024-09-13', remarks = '元N375PH'
 WHERE registration_number = 'JA803B' AND remarks = '2024/09/13抹消、元N375PH';
-- 「2025/04/031抹消」は日付の誤記。4/31 は存在しないので 4/3 とする
UPDATE aircraft SET deregister_date = DATE '2025-04-03', remarks = NULL
 WHERE registration_number = 'JA09MC' AND remarks = '2025/04/031抹消';
-- 備考に日付だけが入っていた。その日付を抹消年月日とする
UPDATE aircraft SET deregister_date = DATE '2023-12-15', remarks = NULL
 WHERE registration_number = 'JA703J' AND remarks = '2023/12/15';
-- JA8324, JA8578 は退役（抹消）だが抹消年月日の記録が無い。抹消年月日は空のままにする

COMMIT;
