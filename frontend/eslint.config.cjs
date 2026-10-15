const path = require('node:path');
const { fixupPluginRules } = require('@eslint/compat');
const globals = require('globals');
const tsParser = require('@typescript-eslint/parser');
const tsPlugin = require('@typescript-eslint/eslint-plugin');
const stylistic = require('@stylistic/eslint-plugin');
const legacy = require('./eslint.rules.cjs');
const policies = require('./eslint.react-policies.cjs');

module.exports = (async () => {
  const modernReact = (await import('@eslint-react/eslint-plugin')).default;
  const reactRules = { ...policies };
  for (const name of [
    'jsx-closing-bracket-location',
    'jsx-tag-spacing',
    'jsx-curly-spacing',
    'jsx-equals-spacing',
    'jsx-indent-props',
    'jsx-max-props-per-line',
    'jsx-pascal-case',
    'jsx-wrap-multilines',
    'jsx-curly-brace-presence',
  ]) {
    reactRules[name] = stylistic.rules[name];
  }
  reactRules['self-closing-comp'] = stylistic.rules['jsx-self-closing-comp'];
  reactRules['jsx-key'] = modernReact.rules['no-missing-key'];
  reactRules['no-direct-mutation-state'] =
    modernReact.rules['no-direct-mutation-state'];
  reactRules['no-unknown-property'] =
    modernReact.rules['dom-no-unknown-property'];
  reactRules['jsx-no-useless-fragment'] =
    modernReact.rules['jsx-no-useless-fragment'];
  reactRules['hook-use-state'] = modernReact.rules['use-state'];

  const replacements = {
    'no-native-reassign': 'no-global-assign',
    'no-negated-in-lhs': 'no-unsafe-negation',
    'no-new-object': 'no-object-constructor',
    'no-spaced-func': '@stylistic/function-call-spacing',
    'no-catch-shadow': 'no-shadow',
  };

  function migrate(rules) {
    return Object.fromEntries(
      Object.entries(rules).flatMap(([name, value]) => {
        const severity = Array.isArray(value) ? value[0] : value;
        if (!severity || severity === 'off') return [];
        // All JSX must be typed; tsc replaces runtime prop-types validation. The
        // general maintained indent rule and Prettier cover the retired JSX rule.
        if (name === 'react/prop-types') return [['react/typed-jsx', value]];
        if (name === 'react/jsx-indent')
          return [['@stylistic/indent', ['error', 2, { SwitchCase: 1 }]]];
        if (name.startsWith('react/')) {
          if (!reactRules[name.slice(6)])
            throw new Error(`Unmapped React policy: ${name}`);
          if (name === 'react/hook-use-state')
            return [
              [
                name,
                [
                  'error',
                  {
                    enforceAssignment: true,
                    enforceSetterName: true,
                    enforceLazyInitialization: false,
                  },
                ],
              ],
            ];
          return [[name, value]];
        }
        if (name === 'import/newline-after-import')
          return [['import-x/newline-after-import', value]];
        return [
          [
            replacements[name] ||
              (stylistic.rules[name] ? `@stylistic/${name}` : name),
            value,
          ],
        ];
      })
    );
  }

  const baseRules = migrate(legacy.rules);
  const typedRules = migrate(legacy.overrides[1].rules);
  // Core rules disabled by TypeScript's recommended config must stay disabled.
  for (const [name, value] of Object.entries(legacy.overrides[1].rules)) {
    if (value === 'off' || value === 0) typedRules[name] = 'off';
  }
  typedRules['react/prop-types'] = 'off';

  return [
    {
      ignores: [
        '**/JsLibraries/**',
        '**/*.css.d.ts',
        'frontend/tests/**',
        'frontend/eslint*.cjs',
      ],
    },
    {
      files: ['frontend/**/*.{js,jsx,cjs,mjs,ts,tsx}'],
      languageOptions: {
        parser: tsParser,
        ecmaVersion: 'latest',
        sourceType: 'module',
        parserOptions: { ecmaFeatures: { jsx: true }, jsxPragma: 'React' },
        globals: {
          ...globals.browser,
          ...globals.node,
          expect: 'readonly',
          chai: 'readonly',
          sinon: 'readonly',
          JSX: 'writable',
        },
      },
      plugins: {
        '@stylistic': stylistic,
        '@typescript-eslint': tsPlugin,
        filenames: fixupPluginRules(require('eslint-plugin-filenames')),
        react: { rules: reactRules },
        'react-hooks': require('eslint-plugin-react-hooks'),
        'simple-import-sort': require('eslint-plugin-simple-import-sort'),
        'import-x': require('eslint-plugin-import-x'),
        prettier: fixupPluginRules(require('eslint-plugin-prettier')),
      },
      settings: {
        react: { version: '18.3.1' },
        'react-x': { version: '18.3.1' },
      },
      rules: { ...baseRules, 'react/typed-jsx': 'error' },
    },
    {
      files: ['frontend/**/*.{ts,tsx}'],
      languageOptions: {
        parserOptions: {
          project: './tsconfig.json',
          tsconfigRootDir: path.resolve(__dirname, '..'),
        },
      },
      rules: { ...require('eslint-config-prettier').rules, ...typedRules },
    },
    {
      files: ['frontend/**/*.js'],
      rules: legacy.overrides[0].rules,
    },
  ];
})();
