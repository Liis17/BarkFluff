using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Redis и production defaults распределённой защиты Identity (ServiceId = 1).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260826110000_AddIdentitySecurityConfiguration")]
    public partial class AddIdentitySecurityConfiguration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 1
                FROM (VALUES
                    ('Redis', ''),
                    ('IdentitySecurity', 'HighRiskRequestsPerMinute'),
                    ('IdentitySecurity', 'SubjectRequestsPerWindow'),
                    ('IdentitySecurity', 'SubjectWindowMinutes'),
                    ('IdentitySecurity', 'FailureLimit'),
                    ('IdentitySecurity', 'FailureWindowMinutes'),
                    ('IdentitySecurity', 'LockoutMinutes'),
                    ('IdentitySecurity', 'CodeAttemptLimit'),
                    ('IdentitySecurity', 'OtpAttemptLimit'),
                    ('IdentitySecurity', 'BackoffBaseMilliseconds'),
                    ('IdentitySecurity', 'BackoffMaxMilliseconds')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 1
                      AND c.""Section"" = v.""Section""
                      AND c.""Key"" = v.""Key""
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 1
                  AND ""EditedBy"" = 'system'
                  AND ""EditedFrom"" = 'migration'
                  AND (""Section"" = 'Redis' AND ""Key"" = ''
                       OR ""Section"" = 'IdentitySecurity');
            ");
        }
    }
}
