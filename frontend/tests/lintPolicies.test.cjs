const assert = require('node:assert/strict');
const path = require('node:path');
const test = require('node:test');
const { ESLint, Linter } = require('eslint');
const parser = require('@typescript-eslint/parser');
const policies = require('../eslint.react-policies.cjs');

function lint(source, rules, filename = 'fixture.tsx') {
  return new Linter().verify(source, [{ files: ['**/*.{tsx,jsx}'], languageOptions: { parser, parserOptions: { ecmaFeatures: { jsx: true } } }, plugins: { react: { rules: policies } }, rules }], { filename });
}

test('stable handlers preserve inline-function, local binding and ref policies', () => {
  const options = { 'react/jsx-no-bind': ['error', { ignoreRefs: true }] };
  assert.equal(lint('function View() { const onClick = () => {}; return <Button onClick={onClick} />; }', options).length, 1);
  assert.equal(lint('function View() { return <Button onClick={() => {}} />; }', options).length, 1);
  assert.equal(lint('function View() { return <Button onClick={handler.bind(this)} />; }', options).length, 1);
  assert.equal(lint('function View() { return <Button onClick={enabled ? onClick : () => {}} />; }', options).length, 1);
  assert.equal(lint('function View() { const onClick = enabled ? handler.bind(this) : other; return <Button onClick={onClick} />; }', options).length, 1);
  assert.equal(lint('const onClick = () => {}; function View() { return <Button onClick={onClick} />; }', options).length, 0, 'stable module handlers preserve their original exemption');
  assert.equal(lint('function View() { const onClick = useCallback(() => {}, []); return <Button onClick={onClick} ref={() => {}} />; }', options).length, 0);
  assert.equal(lint('function View() { const onClick = () => {}; return <Button onClick={onClick} />; }', { 'react/jsx-no-bind': ['error', { allowArrowFunctions: true }] }).length, 0);
});

test('member handler contracts retain the former local-callback exemption', () => {
  const options = { 'react/jsx-handler-names': ['error', { eventHandlerPrefix: 'on', eventHandlerPropPrefix: 'on' }] };
  assert.equal(lint('const view = <Button onClick={handleClick} onChange={props.onChange} ref={fieldRef} />;', options).length, 0);
  assert.equal(lint('const view = <Button onClick={this.handleClick} />;', options).length, 1);
  assert.equal(lint('const view = <Button click={props.onClick} />;', options).length, 1);
  assert.equal(lint('const view = <Button onClick={this.onClick} />;', options).length, 0);
});

test('typed JSX, classic bindings and duplicate-prop checks preserve validation', () => {
  const options = { 'react/typed-jsx': 'error', 'react/jsx-no-undef': 'error', 'react/react-in-jsx-scope': 'error', 'react/jsx-no-duplicate-props': ['error', { ignoreCase: true }] };
  assert.equal(lint('import React from "react"; import Button from "button"; const view = <Button />;', options).length, 0);
  assert.ok(lint('const view = <Unknown foo={1} Foo={2} />;', options).some((message) => message.ruleId === 'react/jsx-no-duplicate-props'));
  assert.ok(lint('const view = <Unknown />;', options).some((message) => message.ruleId === 'react/jsx-no-undef'));
  assert.ok(lint('import React from "react"; const view = <div />;', options, 'fixture.jsx').some((message) => message.ruleId === 'react/typed-jsx'));
  assert.ok(lint('import React from "react"; const view = <>text</>;', options, 'fixture.jsx').some((message) => message.ruleId === 'react/typed-jsx'));
});

test('component declarations, fragments and class lifecycle ordering remain enforced', () => {
  assert.equal(lint('const View = () => <div />;', { 'react/function-component-definition': 'error' }).length, 1);
  assert.equal(lint('function View() { return <div />; }', { 'react/function-component-definition': 'error' }).length, 0);
  assert.equal(lint('const view = <React.Fragment><div /></React.Fragment>;', { 'react/jsx-fragments': 'error' }).length, 1);
  assert.equal(lint('const view = <React.Fragment key="key"><div /></React.Fragment>;', { 'react/jsx-fragments': 'error' }).length, 0);
  assert.equal(lint('class View extends Component { render() {} componentDidMount() {} }', { 'react/sort-comp': 'error' }).length, 1);
  assert.equal(lint('class View extends Component { componentDidMount() {} render() {} }', { 'react/sort-comp': 'error' }).length, 0);
  assert.equal(lint('class One extends Component {} class Two extends Component {}', { 'react/no-multi-comp': ['error', { ignoreStateless: true }] }).length, 1);
});

test('boolean values and reserved/callback prop order preserve the project style', () => {
  assert.equal(lint('const view = <Button disabled />;', { 'react/jsx-boolean-value': ['error', 'always'] }).length, 1);
  assert.equal(lint('const view = <Button disabled={true} />;', { 'react/jsx-boolean-value': ['error', 'always'] }).length, 0);
  const options = { 'react/jsx-sort-props': ['error', { reservedFirst: true, callbacksLast: true, noSortAlphabetically: true }] };
  assert.equal(lint('const view = <Button onClick={onClick} title="Title" />;', options).length, 1);
  assert.equal(lint('const view = <Button title="Title" key="key" />;', options).length, 1);
  assert.equal(lint('const view = <Button key="key" title="Title" onClick={onClick} {...props} name="name" />;', options).length, 0);
});

test('the maintained flat config resolves every active historical policy', async() => {
  const eslint = new ESLint({ overrideConfigFile: path.resolve(__dirname, '../eslint.config.cjs') });
  const config = await eslint.calculateConfigForFile(path.resolve(__dirname, '../src/Components/Error/ErrorBoundary.tsx'));
  for (const rule of ['react/jsx-no-bind', 'react/jsx-handler-names', 'react/typed-jsx', 'react/jsx-key', 'react-hooks/rules-of-hooks', 'react-hooks/exhaustive-deps', 'react/hook-use-state', '@typescript-eslint/no-unused-vars', 'prettier/prettier']) {
    assert.equal(config.rules[rule][0], 2, `${rule} remains an error`);
  }
  assert.equal(config.rules['@stylistic/indent'][0], 0, 'Prettier owns typed JSX indentation without the retired JSX-indent rule');
});
