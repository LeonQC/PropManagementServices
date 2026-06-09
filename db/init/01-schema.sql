CREATE TABLE IF NOT EXISTS properties (
    id TEXT PRIMARY KEY,
    title TEXT,
    slug TEXT,
    property_type TEXT,
    property_subtype TEXT,
    status TEXT,
    total_sqft DOUBLE PRECISION,
    leasable_sqft DOUBLE PRECISION,
    year_built INTEGER,
    lot_size_acres DOUBLE PRECISION,
    unit_count INTEGER,
    asking_price DOUBLE PRECISION,
    cap_rate DOUBLE PRECISION,
    noi DOUBLE PRECISION,
    occupancy_rate DOUBLE PRECISION,
    market_cap_rate_benchmark DOUBLE PRECISION,
    year1_noi_estimate DOUBLE PRECISION,
    description_text TEXT,
    ai_summary TEXT,
    listed_at TEXT,
    updated_at TEXT
);

CREATE TABLE IF NOT EXISTS addresses (
    id TEXT PRIMARY KEY,
    property_id TEXT REFERENCES properties(id) ON DELETE CASCADE,
    street TEXT,
    city TEXT,
    state TEXT,
    zip TEXT,
    metro_area TEXT,
    latitude DOUBLE PRECISION,
    longitude DOUBLE PRECISION,
    neighborhood TEXT
);

CREATE TABLE IF NOT EXISTS property_media (
    id TEXT PRIMARY KEY,
    property_id TEXT REFERENCES properties(id) ON DELETE CASCADE,
    media_type TEXT,
    url TEXT,
    caption TEXT,
    display_order INTEGER,
    is_primary BOOLEAN
);

CREATE TABLE IF NOT EXISTS property_features (
    id TEXT PRIMARY KEY,
    property_id TEXT REFERENCES properties(id) ON DELETE CASCADE,
    feature_category TEXT,
    feature_name TEXT,
    feature_value TEXT
);

CREATE INDEX IF NOT EXISTS idx_addresses_metro_area ON addresses(metro_area);
CREATE INDEX IF NOT EXISTS idx_properties_status ON properties(status);
CREATE INDEX IF NOT EXISTS idx_property_features_property_key ON property_features(property_id, feature_name);
