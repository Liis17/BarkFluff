const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const page = fs.readFileSync(
    path.join(__dirname, '../../../Backend/Barkfluff.AdminPanel/Pages/v2/s3-browser.html'),
    'utf8'
);
const settingsCatalog = fs.readFileSync(
    path.join(__dirname, '../../../Backend/BarkFluff.Settings/Catalog/SettingsCatalog.cs'),
    'utf8'
);

const defaultBucketMatch = page.match(
    /currentBucket = params\.get\('bucket'\) \|\| '([^']+)'/
);
assert.ok(defaultBucketMatch, 'S3 browser must define a default bucket');

const bucketsMatch = settingsCatalog.match(
    /foreach \(var bucket in new\[\] \{([\s\S]*?)\}\)/
);
assert.ok(bucketsMatch, 'Settings catalog must define S3 bucket IDs');

const configuredBuckets = [...bucketsMatch[1].matchAll(/"([^"]+)"/g)].map(match => match[1]);
const defaultBucket = defaultBucketMatch[1];

assert.ok(
    configuredBuckets.includes(defaultBucket),
    `S3 browser default bucket '${defaultBucket}' is not configured`
);

console.log(`PASS: S3 browser default bucket '${defaultBucket}' is configured`);
