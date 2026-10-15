// Project policies without maintained plugin equivalents. Syntax, prop types and
// undefined bindings also remain checked by the required TypeScript compilation.
// Handler naming semantics follow eslint-plugin-react's jsx-handler-names rule
// by Jake Marsh (MIT): https://github.com/jsx-eslint/eslint-plugin-react/tree/v7.37.5
function rule(create, schema = []) {
  return {
    meta: { type: 'problem', schema, messages: { policy: '{{message}}' } },
    create,
  };
}

function report(context, node, message) {
  context.report({ node, messageId: 'policy', data: { message } });
}

function identifier(node) {
  if (!node) return '';
  if (node.type === 'Identifier' || node.type === 'JSXIdentifier')
    return node.name;
  if (node.type === 'MemberExpression' || node.type === 'JSXMemberExpression')
    return identifier(node.property);
  return '';
}

function binding(context, node, name) {
  let scope = context.sourceCode.getScope(node);
  while (scope) {
    if (scope.set.has(name)) return scope.set.get(name);
    scope = scope.upper;
  }
  return undefined;
}

function jsxRoot(node) {
  while (node.type === 'JSXMemberExpression') node = node.object;
  return node;
}

function componentClass(node) {
  return ['Component', 'PureComponent'].includes(identifier(node.superClass));
}

function unwrap(node) {
  while (
    node?.type === 'TSAsExpression' ||
    node?.type === 'TSNonNullExpression'
  )
    node = node.expression;
  return node;
}

function unstableHandler(node, allowArrowFunctions) {
  const expression = unwrap(node);
  if (expression?.type === 'ConditionalExpression') {
    return [expression.test, expression.consequent, expression.alternate].some(
      (branch) => unstableHandler(branch, allowArrowFunctions)
    );
  }
  return (
    expression?.type === 'FunctionExpression' ||
    expression?.type === 'FunctionDeclaration' ||
    (!allowArrowFunctions && expression?.type === 'ArrowFunctionExpression') ||
    (expression?.type === 'CallExpression' &&
      expression.callee.type === 'MemberExpression' &&
      identifier(expression.callee.property) === 'bind')
  );
}

const classOrder = {
  displayName: 0,
  propTypes: 1,
  contextTypes: 2,
  childContextTypes: 3,
  defaultProps: 4,
  constructor: 5,
  state: 6,
  getChildContext: 7,
  getDerivedStateFromProps: 8,
  componentWillMount: 9,
  UNSAFE_componentWillMount: 9,
  componentDidMount: 10,
  componentWillReceiveProps: 11,
  UNSAFE_componentWillReceiveProps: 11,
  shouldComponentUpdate: 12,
  componentWillUpdate: 13,
  UNSAFE_componentWillUpdate: 13,
  getSnapshotBeforeUpdate: 14,
  componentDidUpdate: 15,
  componentWillUnmount: 16,
  componentDidCatch: 17,
  render: 20,
};

module.exports = {
  'typed-jsx': rule((context) => ({
    'JSXElement, JSXFragment'(node) {
      if (!context.filename.endsWith('.tsx'))
        report(
          context,
          node,
          'UI components must use .tsx so props and classic React bindings are type checked.'
        );
    },
  })),
  'jsx-boolean-value': rule(
    (context) => ({
      JSXAttribute(node) {
        if (!node.value)
          report(
            context,
            node,
            'Write boolean JSX props explicitly as {true}.'
          );
      },
    }),
    [{ enum: ['always'] }]
  ),
  'jsx-uses-vars': rule((context) => ({
    JSXOpeningElement(node) {
      const name = jsxRoot(node.name);
      if (/^[A-Z]/.test(name.name))
        context.sourceCode.markVariableAsUsed(name.name, node);
    },
  })),
  'jsx-uses-react': rule((context) => ({
    JSXElement(node) {
      context.sourceCode.markVariableAsUsed('React', node);
    },
    JSXFragment(node) {
      context.sourceCode.markVariableAsUsed('React', node);
    },
  })),
  'jsx-no-undef': rule((context) => ({
    JSXOpeningElement(node) {
      const root = jsxRoot(node.name);
      if (/^[A-Z]/.test(root.name) && !binding(context, node, root.name))
        report(context, root, `Undefined JSX component ${root.name}.`);
    },
  })),
  'react-in-jsx-scope': rule((context) => ({
    JSXElement(node) {
      if (!binding(context, node, 'React'))
        report(
          context,
          node,
          'The classic JSX transform requires React in scope.'
        );
    },
    JSXFragment(node) {
      if (!binding(context, node, 'React'))
        report(
          context,
          node,
          'The classic JSX transform requires React in scope.'
        );
    },
  })),
  'jsx-no-duplicate-props': rule(
    (context) => ({
      JSXOpeningElement(node) {
        const names = new Set();
        for (const attribute of node.attributes) {
          if (attribute.type !== 'JSXAttribute') continue;
          const name = identifier(attribute.name);
          const key = context.options[0]?.ignoreCase
            ? name.toLowerCase()
            : name;
          if (names.has(key))
            report(context, attribute, `Duplicate JSX prop ${name}.`);
          names.add(key);
        }
      },
    }),
    [
      {
        type: 'object',
        properties: { ignoreCase: { type: 'boolean' } },
        additionalProperties: false,
      },
    ]
  ),
  'jsx-no-bind': rule(
    (context) => ({
      JSXAttribute(node) {
        const options = context.options[0] || {};
        if (options.ignoreRefs && identifier(node.name) === 'ref') return;
        let expression = unwrap(node.value?.expression);
        if (expression?.type === 'Identifier') {
          const variable = binding(context, node, expression.name);
          if (
            variable?.scope.type !== 'module' &&
            variable?.scope.type !== 'global'
          ) {
            const definition = variable?.defs[0];
            expression =
              unwrap(definition?.node?.init) ||
              (definition?.type === 'FunctionName'
                ? definition.node
                : expression);
          }
        }
        if (unstableHandler(expression, options.allowArrowFunctions)) {
          report(
            context,
            node,
            'Use a stable named handler instead of binding or defining a function in JSX.'
          );
        }
      },
    }),
    [
      {
        type: 'object',
        properties: {
          ignoreRefs: { type: 'boolean' },
          allowArrowFunctions: { type: 'boolean' },
        },
        additionalProperties: false,
      },
    ]
  ),
  'jsx-handler-names': rule(
    (context) => ({
      JSXAttribute(node) {
        const options = context.options[0] || {};
        const prop = identifier(node.name);
        const expression = unwrap(node.value?.expression);
        const handler = identifier(expression);
        // Preserve the original policy's default: local callbacks are exempt;
        // member handlers and forwarded props retain their on/dispatch contract.
        if (
          !handler ||
          expression.type !== 'MemberExpression' ||
          prop === 'ref'
        )
          return;
        const propPattern = new RegExp(
          `^${options.eventHandlerPropPrefix || 'on'}[A-Z]`
        );
        const handlerPattern = new RegExp(
          `^((props\\.${options.eventHandlerPropPrefix || 'on'})|((.*\\.)?${
            options.eventHandlerPrefix || 'on'
          }))[0-9]*[A-Z]`
        );
        const text = context.sourceCode
          .getText(expression)
          .replace(/\s/g, '')
          .replace(/^this\./, '');
        if (propPattern.test(prop) && !handlerPattern.test(text))
          report(
            context,
            node,
            `Handler ${handler} should use the ${
              options.eventHandlerPrefix || 'on'
            } prefix.`
          );
        else if (handlerPattern.test(text) && !propPattern.test(prop))
          report(
            context,
            node,
            `Callback prop ${prop} should use the ${
              options.eventHandlerPropPrefix || 'on'
            } prefix.`
          );
      },
    }),
    [
      {
        type: 'object',
        properties: {
          eventHandlerPrefix: { type: 'string' },
          eventHandlerPropPrefix: { type: 'string' },
        },
        additionalProperties: false,
      },
    ]
  ),
  'function-component-definition': rule((context) => {
    const reported = new Set();
    return {
      'JSXElement, JSXFragment'(node) {
        let owner = node.parent;
        while (
          owner &&
          ![
            'FunctionDeclaration',
            'FunctionExpression',
            'ArrowFunctionExpression',
          ].includes(owner.type)
        )
          owner = owner.parent;
        if (!owner || reported.has(owner)) return;
        const name = owner.id?.name || owner.parent?.id?.name;
        if (
          name &&
          /^[A-Z]/.test(name) &&
          owner.type !== 'FunctionDeclaration'
        ) {
          reported.add(owner);
          report(
            context,
            owner,
            'Named function components should use function declarations.'
          );
        }
      },
    };
  }),
  'jsx-fragments': rule((context) => ({
    JSXOpeningElement(node) {
      if (
        (node.name.type === 'JSXIdentifier' && node.name.name === 'Fragment') ||
        (node.name.type === 'JSXMemberExpression' &&
          node.name.object.name === 'React' &&
          node.name.property.name === 'Fragment')
      ) {
        if (!node.attributes.length)
          report(
            context,
            node,
            'Use shorthand fragments when no key is required.'
          );
      }
    },
  })),
  'jsx-sort-props': rule(
    (context) => ({
      JSXOpeningElement(node) {
        const options = context.options[0] || {};
        let previous = -1;
        for (const attribute of node.attributes) {
          if (attribute.type === 'JSXSpreadAttribute') {
            previous = -1;
            continue;
          }
          const name = identifier(attribute.name);
          const reserved = [
            'children',
            'dangerouslySetInnerHTML',
            'key',
            'ref',
          ].includes(name);
          const order =
            options.reservedFirst && reserved
              ? 0
              : options.callbacksLast && /^on[A-Z]/.test(name)
              ? 2
              : 1;
          if (order < previous)
            report(
              context,
              attribute,
              'Put reserved props first and callbacks last within each spread group.'
            );
          previous = order;
        }
      },
    }),
    [
      {
        type: 'object',
        properties: {
          callbacksLast: { type: 'boolean' },
          noSortAlphabetically: { const: true },
          reservedFirst: { type: 'boolean' },
        },
        additionalProperties: false,
      },
    ]
  ),
  'prefer-es6-class': rule((context) => ({
    CallExpression(node) {
      if (identifier(node.callee) === 'createClass')
        report(context, node, 'Use an ES class instead of React.createClass.');
    },
  })),
  'no-multi-comp': rule(
    (context) => {
      let first;
      return {
        'ClassDeclaration, ClassExpression'(node) {
          if (!componentClass(node)) return;
          if (first)
            report(
              context,
              node,
              'Keep each stateful class component in its own file.'
            );
          first = node;
        },
      };
    },
    [
      {
        type: 'object',
        properties: { ignoreStateless: { type: 'boolean' } },
        additionalProperties: false,
      },
    ]
  ),
  'sort-comp': rule((context) => ({
    'ClassDeclaration, ClassExpression'(node) {
      if (!componentClass(node)) return;
      let previous = -1;
      for (const member of node.body.body) {
        const name = identifier(member.key);
        const order =
          classOrder[name] ??
          (member.static ? -1 : /^render[A-Z]/.test(name) ? 19 : 18);
        if (order < previous)
          report(
            context,
            member,
            `Class member ${name} is out of order; lifecycle methods precede handlers and render follows them.`
          );
        previous = order;
      }
    },
  })),
};
