// namespace Tyhp.TyhpLang.Binder.PHPBuiltIn
// {
//     public static class BuiltInSymbols
//     {
//         public static IEnumerable<Symbol> GetAll(Func<Tyhp.TyhpLang.Ast.Interfaces.IBaseAst> definer)
//         {
//             return BuiltInSymbols.GetTypes(definer)
//                 .Concat(BuiltInSymbols.GetConstants(definer))
//                 .Concat(BuiltInSymbols.GetGlobals(definer));
//         }

//         public static IEnumerable<Symbol> GetTypes(Func<Tyhp.TyhpLang.Ast.Interfaces.IBaseAst> definer)
//         {
//             return new List<Symbol>() {
//                 new Symbol("void", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("null", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("array", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("bool", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("callable", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("false", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("true", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("float", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("int", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("iterable", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("mixed", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("never", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("object", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("resource", definer(), Enum.SymbolType.BuiltInType),
//                 new Symbol("string", definer(), Enum.SymbolType.BuiltInType),
//             };
//         }

//         public static IEnumerable<Symbol> GetConstants(Func<Tyhp.TyhpLang.Ast.Interfaces.IBaseAst> definer)
//         {
//             return new List<Symbol>() {
//                 new Symbol("__LINE__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__FUNCTION__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__FILE__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__METHOD__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__CLASS__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__DIR__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__TRAIT__", definer(), Enum.SymbolType.MagicConstant),
//                 new Symbol("__NAMESPACE__", definer(), Enum.SymbolType.MagicConstant),
//             };
//         }

//         public static IEnumerable<Symbol> GetGlobals(Func<Tyhp.TyhpLang.Ast.Interfaces.IBaseAst> definer)
//         {
//             return new List<Symbol>() {
//                 new Symbol("$GLOBALS", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_SERVER", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_GET", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_POST", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_FILES", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_COOKIE", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_SESSION", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_REQUEST", definer(), Enum.SymbolType.SuperGlobalVariable),
//                 new Symbol("$_ENV", definer(), Enum.SymbolType.SuperGlobalVariable),
//             };
//         }
//     }
// }