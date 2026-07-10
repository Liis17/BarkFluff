using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialConfigurationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- RunSettings - Port for different services
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 0),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 1),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 2),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 3),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 4),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 5),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 6),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 7),
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 8),
                    ('RunSettings', 'Http1Port', '', NOW(), 'system', 'migration', 5),
                    ('RunSettings', 'Host', '', NOW(), 'system', 'migration', 0),

                    -- JwtSettings
                    ('JwtSettings', 'SecretKey', '', NOW(), 'system', 'migration', 0),
                    ('JwtSettings', 'Issuer', '', NOW(), 'system', 'migration', 0),
                    ('JwtSettings', 'Audience', '', NOW(), 'system', 'migration', 0),
                    ('JwtSettings', 'ExpiryMinutes', '', NOW(), 'system', 'migration', 0),

                    -- RabbitMQ
                    ('RabbitMQ', 'VirtualHost', '', NOW(), 'system', 'migration', 0),
                    ('RabbitMQ', 'Host', '', NOW(), 'system', 'migration', 0),
                    ('RabbitMQ', 'Username', '', NOW(), 'system', 'migration', 0),
                    ('RabbitMQ', 'Password', '', NOW(), 'system', 'migration', 0),

                    -- FilesService
                    ('FilesService', 'Token', '', NOW(), 'system', 'migration', 2),
                    ('FilesService', 'Host', '', NOW(), 'system', 'migration', 2),
                    ('FilesService', 'Token', '', NOW(), 'system', 'migration', 6),
                    ('FilesService', 'Host', '', NOW(), 'system', 'migration', 6),

                    -- ServerColor
                    ('ServerColor', 'Lite', '', NOW(), 'system', 'migration', 3),
                    ('ServerColor', 'Main', '', NOW(), 'system', 'migration', 3),
                    ('ServerColor', 'Hard', '', NOW(), 'system', 'migration', 3),

                    -- Database connection strings
                    ('UsersDb', '', '', NOW(), 'system', 'migration', 2),
                    ('IdentityDb', '', '', NOW(), 'system', 'migration', 1),
                    ('FilesDb', '', '', NOW(), 'system', 'migration', 5),
                    ('MessagesDb', '', '', NOW(), 'system', 'migration', 6),

                    -- UsersService
                    ('UsersService', 'Token', '', NOW(), 'system', 'migration', 0),
                    ('UsersService', 'Host', '', NOW(), 'system', 'migration', 0),

                    -- TempFiles
                    ('TempFiles', 'ExpiresAt', '', NOW(), 'system', 'migration', 5),

                    -- Email
                    ('Email', 'Host', '', NOW(), 'system', 'migration', 4),
                    ('Email', 'Port', '', NOW(), 'system', 'migration', 4),
                    ('Email', 'SenderEmail', '', NOW(), 'system', 'migration', 4),
                    ('Email', 'SenderPassword', '', NOW(), 'system', 'migration', 4),

                    -- Minio
                    ('Minio', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('Minio', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('Minio', 'AccessKey', '', NOW(), 'system', 'migration', 5),

                    -- Redis
                    ('Redis', '', '', NOW(), 'system', 'migration', 6),

                    -- NavigatorUrl
                    ('NavigatorUrl', '', '', NOW(), 'system', 'migration', 0);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Delete all seeded configuration keys
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
