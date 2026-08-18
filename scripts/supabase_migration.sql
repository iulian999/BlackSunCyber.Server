-- ============================================================
-- BlackSun Cyber — Migrare Supabase pentru noile funcționalități
-- Rulează aceste comenzi în Supabase Dashboard > SQL Editor
-- ============================================================

-- ============================================================
-- 1. Profiluri Jucători + Economie SunCoins
-- ============================================================

CREATE TABLE IF NOT EXISTS player_profiles (
    id               BIGSERIAL PRIMARY KEY,
    nickname         TEXT UNIQUE NOT NULL,
    sun_coins        INTEGER NOT NULL DEFAULT 0,
    total_minutes_played INTEGER NOT NULL DEFAULT 0,
    total_sessions   INTEGER NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index pentru leaderboard rapid
CREATE INDEX IF NOT EXISTS idx_player_profiles_coins ON player_profiles(sun_coins DESC);
CREATE INDEX IF NOT EXISTS idx_player_profiles_minutes ON player_profiles(total_minutes_played DESC);

-- Istoricul tranzacțiilor SunCoins (pozitiv = câștigat, negativ = cheltuit)
CREATE TABLE IF NOT EXISTS sun_coin_transactions (
    id        BIGSERIAL PRIMARY KEY,
    nickname  TEXT NOT NULL,
    amount    INTEGER NOT NULL,
    reason    TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_sun_coin_transactions_nickname ON sun_coin_transactions(nickname);

-- ============================================================
-- 2. Chat Direct Client ↔ Admin
-- ============================================================

CREATE TABLE IF NOT EXISTS chat_messages (
    id          BIGSERIAL PRIMARY KEY,
    station_id  INTEGER NOT NULL,
    sender      TEXT NOT NULL CHECK (sender IN ('client', 'admin')),
    sender_name TEXT,
    message     TEXT NOT NULL,
    is_read     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_chat_messages_station ON chat_messages(station_id, created_at ASC);
CREATE INDEX IF NOT EXISTS idx_chat_messages_unread ON chat_messages(sender, is_read) WHERE is_read = FALSE;

-- ============================================================
-- 3. Rezervări Remote
-- ============================================================

CREATE TABLE IF NOT EXISTS bookings (
    id               BIGSERIAL PRIMARY KEY,
    station_id       INTEGER,  -- NULL = nu a ales PC-ul încă
    nickname         TEXT NOT NULL,
    phone            TEXT,
    scheduled_at     TIMESTAMPTZ NOT NULL,
    duration_minutes INTEGER NOT NULL DEFAULT 60,
    status           TEXT NOT NULL DEFAULT 'pending'
                     CHECK (status IN ('pending', 'confirmed', 'cancelled', 'completed')),
    note             TEXT,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_bookings_scheduled ON bookings(scheduled_at ASC);
CREATE INDEX IF NOT EXISTS idx_bookings_status ON bookings(status) WHERE status IN ('pending', 'confirmed');

-- ============================================================
-- Verificare
-- ============================================================
SELECT 'player_profiles' AS table_name, COUNT(*) FROM player_profiles
UNION ALL
SELECT 'sun_coin_transactions', COUNT(*) FROM sun_coin_transactions
UNION ALL
SELECT 'chat_messages', COUNT(*) FROM chat_messages
UNION ALL
SELECT 'bookings', COUNT(*) FROM bookings;
