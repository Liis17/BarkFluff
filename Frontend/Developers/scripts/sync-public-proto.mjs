import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '../../..');
const sourceDirectory = resolve(repositoryRoot, 'Shared/BarkFluff.Proto');
const targetDirectory = resolve(scriptDirectory, '../proto');
const checkOnly = process.argv.includes('--check');

const publicProtoFiles = [
  'developers_api.proto',
  'identity_api.proto',
  'shared.proto',
];

let hasDrift = false;

for (const fileName of publicProtoFiles) {
  const sourcePath = resolve(sourceDirectory, fileName);
  const targetPath = resolve(targetDirectory, fileName);
  const source = await readFile(sourcePath);

  try {
    const target = await readFile(targetPath);
    if (Buffer.compare(source, target) === 0) continue;
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }

  hasDrift = true;
  if (checkOnly) {
    console.error(`Proto snapshot drift: ${fileName}`);
  } else {
    await writeFile(targetPath, source);
    console.log(`Synced ${fileName}`);
  }
}

if (checkOnly && hasDrift) {
  console.error('Run npm run sync-proto from Frontend/Developers to update snapshots.');
  process.exitCode = 1;
}
