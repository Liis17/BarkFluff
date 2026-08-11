const fs = require('node:fs/promises');
const path = require('node:path');
const esbuild = require('esbuild');

const projectRoot = path.resolve(__dirname, '..');
const outputDirectory = process.argv[2]
    ? path.resolve(process.cwd(), process.argv[2])
    : path.join(projectRoot, 'wwwroot', 'js', 'app');

async function build() {
    await fs.mkdir(outputDirectory, { recursive: true });
    const result = await esbuild.build({
        entryPoints: [path.join(__dirname, 'app-bundle-entry.js')],
        bundle: true,
        entryNames: 'app-[hash]',
        format: 'iife',
        metafile: true,
        minify: true,
        outdir: outputDirectory,
        sourcemap: true,
        target: 'es2020'
    });

    const bundlePath = Object.keys(result.metafile.outputs).find(function (file) {
        return file.endsWith('.js');
    });
    if (!bundlePath) throw new Error('esbuild did not produce an application bundle');

    const bundleName = path.basename(bundlePath);
    await fs.writeFile(
        path.join(outputDirectory, 'app-manifest.json'),
        JSON.stringify({ src: '/js/app/' + bundleName }) + '\n'
    );
    process.stdout.write('Built /js/app/' + bundleName + '\n');
}

build().catch(function (error) {
    console.error(error);
    process.exitCode = 1;
});
