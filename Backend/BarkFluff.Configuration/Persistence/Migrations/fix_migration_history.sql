-- This script fixes the migration history after renaming migrations to correct the order
-- Run this script on the Configuration database if you're getting errors about missing tables

-- Update the migration entries with old timestamps to new timestamps
UPDATE "__EFMigrationsHistory"
SET "MigrationId" = '20250509000000_SeedBeaconServerProps'
WHERE "MigrationId" = '20250116000000_SeedBeaconServerProps';

UPDATE "__EFMigrationsHistory"
SET "MigrationId" = '20250510000000_AddServerPropsLocation'
WHERE "MigrationId" = '20250201000000_AddServerPropsLocation';

-- Alternative: Drop and recreate the database
-- This is cleaner but will lose any configuration data you've entered
-- DROP DATABASE IF EXISTS your_database_name;
-- CREATE DATABASE your_database_name;
