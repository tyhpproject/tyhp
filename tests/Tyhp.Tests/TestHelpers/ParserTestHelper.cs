using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.TestHelpers;

public static class ParserTestHelper
{
    public static ParseResult ParseTyhpContent(string tyhpSource, string fileName = "test.tyhp", bool tagless = false)
        => Parse(tyhpSource, fileName, ParseMode.Tyhp, tagless);

    public static ParseResult ParseTyhpdefContent(string tyhpdefSource, string fileName = "test.tyhpdef", bool tagless = false)
        => Parse(tyhpdefSource, fileName, ParseMode.Tyhpdef, tagless);

    public static ParseResult ParsePhpContent(string phpSource, string fileName = "test.php")
        => Parse(phpSource, fileName, ParseMode.Php, tagless: false);

    public static ParseResult ParseFile(string filePath, bool? tagless = null)
    {
        var fullPath = Path.GetFullPath(filePath);
        var content = File.ReadAllText(fullPath);
        var mode = ResolveParseMode(fullPath);
        var useTagless = tagless ?? false;
        return Parse(content, fullPath, mode, useTagless);
    }

    private static ParseMode ResolveParseMode(string filePath)
    {
        if (filePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMode.Tyhpdef;
        }

        if (filePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMode.Tyhp;
        }

        return ParseMode.Php;
    }

    private static ParseResult Parse(string content, string fileName, ParseMode mode, bool tagless)
    {
        var result = new ParseResult();
        try
        {
            result.Ast = Tyhpdef.ParseContent(content, fileName, mode, result.Diagnostics, tagless);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result.Diagnostics.AddError(
                Tyhp.Domain.Exceptions.MessageCode.ParserCompileAborted,
                fileName,
                0,
                0,
                ex.GetType().Name,
                ex.Message);
        }

        return result;
    }
}
