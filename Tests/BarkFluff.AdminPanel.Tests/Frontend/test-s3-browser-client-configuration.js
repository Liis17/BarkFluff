const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const service = fs.readFileSync(
    path.join(__dirname, '../../../Backend/Barkfluff.AdminPanel/Services/S3BrowserService.cs'),
    'utf8'
);

assert.match(
    service,
    /Region = g\.FirstOrDefault\(c => c\.Key == "Region"\)\?\.Value \?\? ""/,
    'S3 browser must load the bucket signing region from configuration'
);
assert.match(
    service,
    /RequestChecksumCalculation = RequestChecksumCalculation\.WHEN_REQUIRED/,
    'S3 browser must only calculate checksums when S3 requires them'
);
assert.match(
    service,
    /ResponseChecksumValidation = ResponseChecksumValidation\.WHEN_REQUIRED/,
    'S3 browser must only validate response checksums when S3 requires them'
);
assert.match(
    service,
    /s3Config\.AuthenticationRegion = config\.Region/,
    'S3 browser must use the configured region for SigV4 authentication'
);

console.log('PASS: S3 browser client uses compatible region and checksum settings');
