using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Shared <c>extends</c> lookup for named structs (<see cref="TyhpStructDeclAst"/>,
    /// <c>type Name = struct extends Parent { … }</c>) and class/tyhpdef object decls.
    /// <c>extends</c> is parsed as a raw <see cref="IClassName"/>, not an
    /// <see cref="ITypeExpression"/>, so <c>ObjectDeclarationSymbol.ExtendsType</c> is usually
    /// null and callers must read the declaring AST.
    /// </summary>
    public static class StructExtends
    {
        public static IClassName? FromDeclaringNode(IBase2Ast? node) =>
            node switch
            {
                PhpObjectTypeDeclAst { Extends: { } className } => className,
                TyhpStructDeclAst { Extends: { } className } => className,
                TyhpStructShapeAst { Extends: { } className } => className,
                TyhpTypeAliasAst alias => FromDeclaringNode(alias.StructShape),
                TyhpdefImportObjectDeclAst { Extends: IClassName className } => className,
                _ => null,
            };

        public static IBase2Ast? ExtendsAstNode(IBase2Ast? declaringNode) =>
            FromDeclaringNode(declaringNode) as IBase2Ast;
    }
}
