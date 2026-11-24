-- adsb.lol の履歴ダンプから切り出したフライト（RefreshAdsbFlightJob が日ごとに入れ替える）
-- data_date   : ダンプのデータ日（UTC）。日本時間ではその日の 9:00〜翌 9:00
-- leg_no      : その日の何本目か（1 から）
-- from_source / to_source : route（路線の空港が軌跡の端の近くにあった）/ route_unverified（端が巡航中で確かめられず路線どおり）
--                           / nearest（軌跡の端に一番近い空港）/ none（決められない）
-- 時刻はすべて日本時間
CREATE TABLE adsb_flight (
    registration_number varchar(10)      NOT NULL,
    data_date           date             NOT NULL,
    leg_no              integer          NOT NULL,
    icao_hex            varchar(6)       NOT NULL,
    callsign            varchar(10),
    flight_number       varchar(10),
    from_code           varchar(8),
    to_code             varchar(8),
    from_ap             varchar(100),
    to_ap               varchar(100),
    from_source         varchar(20),
    to_source           varchar(20),
    departure_time      timestamp        NOT NULL,
    arrival_time        timestamp        NOT NULL,
    landed              boolean          NOT NULL,
    status              varchar(50),
    start_lat           double precision NOT NULL,
    start_lon           double precision NOT NULL,
    end_lat             double precision NOT NULL,
    end_lon             double precision NOT NULL,
    end_altitude        integer,
    updated_at          timestamp        NOT NULL,
    PRIMARY KEY (registration_number, data_date, leg_no)
);
CREATE INDEX adsb_flight_data_date ON adsb_flight (data_date);
