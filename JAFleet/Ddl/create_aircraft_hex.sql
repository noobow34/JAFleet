-- 機体記号と ICAO 24bit アドレス（ADS-B の hex）の対応（RefreshAircraftHexJob が埋める）
-- registration_number : 機体記号（aircraft.registration_number と同じ値。FK は張らない。抹消済みの機体も残してよい）
-- icao_hex            : 小文字6桁（例 86dcbc）
-- source              : 'adsblol'（adsb.lol の履歴ダンプのレジで覚えた） / 'adsbdb'（ADSBdb で引いた）
-- last_seen_date      : ダンプでレジ付きで最後に見えた日（UTC）。ADSBdb で引いただけなら NULL
CREATE TABLE aircraft_hex (
    registration_number varchar(10) PRIMARY KEY,
    icao_hex            varchar(6)  NOT NULL,
    source              varchar(20) NOT NULL,
    last_seen_date      date,
    updated_at          timestamp   NOT NULL
);
CREATE INDEX aircraft_hex_icao_hex ON aircraft_hex (icao_hex);
