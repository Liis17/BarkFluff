const globals = require('globals');
const path = require('node:path');

const correctnessRules = {
    eqeqeq: ['error', 'always', { null: 'ignore' }],
    'no-constant-binary-expression': 'error',
    'no-debugger': 'error',
    'no-dupe-else-if': 'error',
    'no-dupe-keys': 'error',
    'no-redeclare': 'error',
    'no-undef': 'error',
    'no-unreachable': 'error',
    'no-unused-vars': ['warn', { args: 'none', ignoreRestSiblings: true }]
};

module.exports = [
    {
        ignores: [
            'node_modules/**',
            'wwwroot/js/app/app-????????.js',
            'wwwroot/js/proto/**',
            'wwwroot/js/vendor/**'
        ]
    },
    {
        basePath: path.resolve(__dirname, '..'),
        files: ['wwwroot/js/app/**/*.js'],
        languageOptions: {
            ecmaVersion: 'latest',
            sourceType: 'script',
            globals: {
                ...globals.browser,
                BF: 'writable'
            }
        },
        linterOptions: {
            reportUnusedDisableDirectives: 'error'
        },
        rules: correctnessRules
    },
    {
        basePath: path.resolve(__dirname, '..'),
        files: ['scripts/build-app.js'],
        languageOptions: {
            ecmaVersion: 'latest',
            sourceType: 'commonjs',
            globals: globals.node
        },
        rules: correctnessRules
    },
    {
        basePath: path.resolve(__dirname, '..'),
        files: ['scripts/app-bundle-entry.js'],
        languageOptions: {
            ecmaVersion: 'latest',
            sourceType: 'module'
        },
        rules: correctnessRules
    }
];
