-- 路線と空港（VRS standing-data、CC0。RefreshFlightRouteJob が丸ごと入れ替える）
-- flight_route : callsign ごとの路線。airport_codes は ICAO を飛ぶ順に '-' でつないだもの（経由便は3つ以上）
-- airport      : 空港。code は ICAO（無ければ IATA）。location は英語の都市名
CREATE TABLE flight_route (
    callsign      varchar(10)  PRIMARY KEY,
    airline_icao  varchar(4)   NOT NULL,
    flight_number varchar(10)  NOT NULL,
    airport_codes varchar(200) NOT NULL,
    updated_at    timestamp    NOT NULL
);

CREATE TABLE airport (
    code          varchar(8)       PRIMARY KEY,
    name          varchar(200)     NOT NULL,
    icao          varchar(4),
    iata          varchar(3),
    location      varchar(200),
    country_iso2  varchar(2),
    latitude      double precision NOT NULL,
    longitude     double precision NOT NULL,
    altitude_feet integer,
    updated_at    timestamp        NOT NULL
);
CREATE INDEX airport_iata ON airport (iata);
