const globals = require('globals');
const path = require('node:path');

module.exports = [
    {
        ignores: [
            'node_modules/**',
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
        rules: {
            'eqeqeq': ['error', 'always', { null: 'ignore' }],
            'no-constant-binary-expression': 'error',
            'no-debugger': 'error',
            'no-dupe-else-if': 'error',
            'no-dupe-keys': 'error',
            'no-redeclare': 'error',
            'no-undef': 'error',
            'no-unreachable': 'error',
            'no-unused-vars': ['warn', { args: 'none', ignoreRestSiblings: true }]
        }
    }
];
