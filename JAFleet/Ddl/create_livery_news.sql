-- 特別塗装ニュースのチェック結果と、そこから作る機体更新の候補
-- livery_news         : Aviation WireのRSSで拾った記事1本につき1行。特別塗装の記事でなかったものも残す
--                       （同じ記事をGeminiへ二度投げないための印になるため）
-- livery_news_candidate: 記事1本につき機体の数だけできる更新候補。/LiveryNewsで承認するとaircraftへ反映する
CREATE TABLE livery_news (
    news_id        serial       PRIMARY KEY,
    news_url       varchar(512) NOT NULL UNIQUE,
    title          varchar(512),
    published_at   timestamp,
    checked_at     timestamp    NOT NULL,
    special_livery boolean      NOT NULL,
    reason         text,
    gemini_result  text
);

CREATE INDEX idx_livery_news_published_at ON livery_news (published_at DESC);

CREATE TABLE livery_news_candidate (
    candidate_id        serial      PRIMARY KEY,
    news_id             integer     NOT NULL REFERENCES livery_news (news_id) ON DELETE CASCADE,
    registration_number varchar(10),
    livery_name         varchar(255),
    current_livery      varchar(255),
    status              varchar(2)  NOT NULL DEFAULT '0',
    applied_livery      varchar(255),
    created_at          timestamp   NOT NULL,
    resolved_at         timestamp
);

CREATE INDEX idx_livery_news_candidate_status ON livery_news_candidate (status, created_at DESC);
CREATE INDEX idx_livery_news_candidate_news_id ON livery_news_candidate (news_id);
