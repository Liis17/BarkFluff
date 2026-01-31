# Migration Order Fix

## Problem
The Entity Framework migrations for the Configuration service were created with incorrect timestamps, causing them to run in the wrong order:

- `20250116000000_SeedBeaconServerProps` (Jan 16, 2025) - tries to INSERT into Configurations table
- `20250201000000_AddServerPropsLocation` (Feb 1, 2025) - tries to INSERT into Configurations table
- `20250508111334_AddConfiguration` (May 8, 2025) - **creates** the Configurations table

This caused the error: `relation "Configurations" does not exist` because the seed migrations ran before the table was created.

## Solution
The migrations have been renamed to run in the correct order:

1. `20250508111334_AddConfiguration` - Creates the Configurations table
2. `20250509000000_SeedBeaconServerProps` - Seeds initial server props (renamed from 20250116000000)
3. `20250510000000_AddServerPropsLocation` - Adds location property (renamed from 20250201000000)
4. `20251123000000_SeedInitialConfigurationKeys` - Seeds all other configuration keys

## Fixing Your Database

You have two options:

### Option 1: Drop and Recreate the Database (Recommended for Development)

This is the cleanest solution but **will lose all your configuration data**:

```bash
# Connect to PostgreSQL
psql -U your_username -h your_host

# Drop the database (replace with your actual database name)
DROP DATABASE your_configuration_database;

# Create it again
CREATE DATABASE your_configuration_database;

# Exit psql
\q

# Restart the Configuration service - it will automatically apply all migrations in the correct order
```

### Option 2: Update the Migration History Table

If you want to keep your existing data, you can manually fix the migration history:

```bash
# Run the fix script on your Configuration database
psql -U your_username -d your_configuration_database -f Backend/BarkFluff.Configuration/Persistence/Migrations/fix_migration_history.sql
```

Or manually execute:

```sql
UPDATE "__EFMigrationsHistory"
SET "MigrationId" = '20250509000000_SeedBeaconServerProps'
WHERE "MigrationId" = '20250116000000_SeedBeaconServerProps';

UPDATE "__EFMigrationsHistory"
SET "MigrationId" = '20250510000000_AddServerPropsLocation'
WHERE "MigrationId" = '20250201000000_AddServerPropsLocation';
```

## Files Changed

- Renamed: `20250116000000_SeedBeaconServerProps.cs` → `20250509000000_SeedBeaconServerProps.cs`
- Renamed: `20250201000000_AddServerPropsLocation.cs` → `20250510000000_AddServerPropsLocation.cs`
- Renamed: `20250201000000_AddServerPropsLocation.Designer.cs` → `20250510000000_AddServerPropsLocation.Designer.cs`
- Updated: Migration attribute in `20250510000000_AddServerPropsLocation.Designer.cs`
- Created: `fix_migration_history.sql` - Script to update migration history

## Verification

After applying the fix, restart the Configuration service. It should start successfully without migration errors.
