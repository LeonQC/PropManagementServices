-- One-time baseline for a database that already has the schema + data created by
-- the old SQL init scripts. It registers the InitialCreate migration as already
-- applied so EF Core's Migrate() won't try to recreate existing tables.
--
-- Run ONCE against the existing database, BEFORE starting the app with the new
-- migrate-on-startup code:
--   docker exec -i proptrackservices-listings-db-1 \
--     psql -U proptrack -d proptrack_listings < db/baseline-existing-db.sql
--
-- Fresh databases do NOT need this — EF builds them from the migration directly.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260621192005_InitialCreate', '9.0.5')
ON CONFLICT (migration_id) DO NOTHING;
