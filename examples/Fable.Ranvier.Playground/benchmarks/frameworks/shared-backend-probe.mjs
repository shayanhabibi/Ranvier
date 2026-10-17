export default function sharedBackendProbe({ types: t }) {
  return {
    visitor: {
      NewExpression(path) {
        const original = path.node.callee;
        if (!t.isClassExpression(original) || !t.isIdentifier(original.superClass, { name: "Backend" })) return;
        const methods = original.body.body.filter(method => method.kind !== "constructor");
        const program = path.findParent(parent => parent.isProgram());
        const name = program.scope.generateUidIdentifier("DiagnosticSharedBackend");
        const handlers = t.identifier("handlers");
        const constructor = t.classMethod("constructor", t.identifier("constructor"), [handlers], t.blockStatement([
          t.expressionStatement(t.callExpression(t.super(), [])),
          t.expressionStatement(t.assignmentExpression("=", t.memberExpression(t.thisExpression(), handlers), handlers))
        ]));
        const shared = methods.map(method => t.classMethod("method", t.cloneNode(method.key), method.params.map(param => t.cloneNode(param)), t.blockStatement([
          t.returnStatement(t.callExpression(t.memberExpression(t.memberExpression(t.thisExpression(), handlers), t.cloneNode(method.key), true), method.params.map(param => t.cloneNode(param))))
        ])));
        const callbacks = methods.map(method => t.objectProperty(t.cloneNode(method.key), t.arrowFunctionExpression(method.params.map(param => t.cloneNode(param)), t.cloneNode(method.body, true))));
        path.replaceWith(t.newExpression(name, [t.objectExpression(callbacks)]));
        program.pushContainer("body", t.classDeclaration(name, original.superClass, t.classBody([constructor, ...shared])));
      }
    }
  };
}
