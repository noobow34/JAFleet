-- 機体情報の更新の予約（/AircraftReservation、単票編集の「予約日」、特別塗装ニュースの承認）
-- apply_date が来たら日次ジョブ ApplyAircraftReservationJob が aircraft へ反映する。
-- changes は変える項目だけの差分（JSON）。予約から反映までに別の項目を直しても巻き戻らない。
-- status: 0=予約中 1=反映済み 2=取消 3=失敗 4=差分なし（JAFleet.Commons の AircraftReservationStatus）
CREATE TABLE aircraft_reservation (
    reservation_id      serial       PRIMARY KEY,
    registration_number varchar(10)  NOT NULL,
    apply_date          date         NOT NULL,
    changes             text         NOT NULL,
    write_history       boolean      NOT NULL DEFAULT true,
    source              varchar(512),
    status              varchar(2)   NOT NULL DEFAULT '0',
    created_at          timestamp    NOT NULL,
    resolved_at         timestamp,
    result_message      text
);

CREATE INDEX idx_aircraft_reservation_status ON aircraft_reservation (status, apply_date);
CREATE INDEX idx_aircraft_reservation_reg ON aircraft_reservation (registration_number);

-- 特別塗装ニュースから読んだ運航開始日。承認した時点で未来なら予約になる
ALTER TABLE livery_news_candidate ADD COLUMN start_date date;

-- 反映ジョブ。毎日 0 時 5 分に、その日（とそれ以前の取りこぼし）の予約を反映する
INSERT INTO scheduler_def (class_name, cron_def, enabled)
SELECT 'JAFleet.Jobs.ApplyAircraftReservationJob', '0 5 0 * * ?', true
WHERE NOT EXISTS (SELECT 1 FROM scheduler_def WHERE class_name = 'JAFleet.Jobs.ApplyAircraftReservationJob');
