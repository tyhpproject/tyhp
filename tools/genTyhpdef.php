<?php

declare(strict_types=1);

ini_set('memory_limit', '8G');

$extList = [
    'Core',
    'date',
    'filter',
    'hash',
    'json',
    'libxml',
    'openssl',
    'pcntl',
    'pcre',
    'random',
    'Reflection',
    'session',
    'sodium',
    'SPL',
    'standard',
    'zlib',
];

$basePath =  \realpath(\getcwd() ?: '.');

@mkdir($basePath . '/tyhpdef_gen/' . \PHP_VERSION, 0777, true);

$tyhpdefList = [];

$languages = [
    'en',
    'de',
    'es',
    'fr',
    'it',
    'ja',
    'pt_BR',
    'ru',
    'tr',
    'uk',
    'zh',
];

foreach ($languages as $language) {
    @mkdir($basePath . '/tyhpdef_gen/' . \PHP_VERSION . '/' . $language, 0777, true);
    $genTyhpdef = new GenerateTyhpdef();
    GenerateTyhpdef::resetDocs();

    foreach ($extList as $extName) {
        $tyhpdefList[$extName] = $genTyhpdef->process(
            $extName,
            "https://www.php.net/distributions/manual/php_manual_" . $language . ".html.gz",
            $language,
            function ($msg) {
                echo $msg;
            }
        );
    }

    foreach ($tyhpdefList as $extName => $base64) {
        $code = \base64_decode($base64);
        echo $basePath . '/tyhpdef_gen/' . \PHP_VERSION . '/' . $language . '/' . $extName . '.tyhpdef' . \PHP_EOL;
        \file_put_contents($basePath . '/tyhpdef_gen/' . \PHP_VERSION . '/' . $language . '/' . $extName . '.tyhpdef', $code);
    }
}

// $p = "        ";
// $list = [];
// $all = [];
// $allKeyed = [];
// foreach ($tyhpdefList as $extName => $base64) {
    // $base64 = \str_split($base64, 140);
    // $base64 = \implode("\" +\n            \"", $base64);
    // $list[] = "public static string Ext" . \ucfirst($extName) . " => Tyhpdef.Decompress(\n            \"" . $base64 . "\");";
    // $all[] = "Tyhpdef.Ext" . \ucfirst($extName);
    // $allKeyed[] = '{ "__php_ext_' . \strtolower($extName) . '", Tyhpdef.Ext' . \ucfirst($extName) . ' }';
// }

// $listStr = \implode("\n        ", $list);
// $allStr = \implode(",\n            ", $all);
// $allKeyedStr = \implode(",\n            ", $allKeyed);

// \file_put_contents("PHP.Tyhpdef.cs", <<<EOF
// using System.IO.Compression;
// using System.Collections.ObjectModel;

// namespace Tyhp.TyhpLang.Binder.PHPBuiltIn
// {
//     internal static class Tyhpdef
//     {
//         public static List<string> All => new List<string>() {
//             $allStr
//         };

//         public static ReadOnlyDictionary<string, string> AllKeyed => (new Dictionary<string, string>() {
//             $allKeyedStr
//         }).AsReadOnly();

//         $listStr

//         private static string Decompress(string encoded)
//         {
//             var encodedBytes = Convert.FromBase64String(encoded);
//             using (var mem = new MemoryStream())
//             {
//                 mem.Write(encodedBytes, 0, encodedBytes.Length);

//                 mem.Position = 0;

//                 using (var gzip = new GZipStream(mem, CompressionMode.Decompress))
//                 using (var reader = new StreamReader(gzip))
//                 {
//                     return reader.ReadToEnd();
//                 }
//             }
//         }
//     }
// }
// EOF
// );

// Tyhp builtins
// $tyhpdefList = [];
// $tyhpdefList['types'] = \base64_encode(\gzcompress(\file_get_contents("../Tyhp/TyhpSpec/tyhpTypes.tyhpdef"), -1, \ZLIB_ENCODING_GZIP));

// $p = "        ";
// $list = [];
// $all = [];
// $allKeyed = [];
// foreach ($tyhpdefList as $extName => $base64) {
//     $base64 = \str_split($base64, 140);
//     $base64 = \implode("\" +\n            \"", $base64);
//     $list[] = "public static string Ext" . \ucfirst($extName) . " => Tyhpdef.Decompress(\n            \"" . $base64 . "\");";
//     $all[] = "Tyhpdef.Ext" . \ucfirst($extName);
//     $allKeyed[] = '{ "__tyhp_' . \strtolower($extName) . '", Tyhpdef.Ext' . \ucfirst($extName) . ' }';
// }

// $listStr = \implode("\n        ", $list);
// $allStr = \implode(",\n            ", $all);
// $allKeyedStr = \implode(",\n            ", $allKeyed);

// \file_put_contents("Tyhp.Tyhpdef.cs", <<<EOF
// using System.IO.Compression;
// using System.Collections.ObjectModel;

// namespace Tyhp.TyhpLang.Binder.TyhpBuiltIn
// {
//     internal static class Tyhpdef
//     {
//         public static List<string> All => new List<string>() {
//             $allStr
//         };

//         public static ReadOnlyDictionary<string, string> AllKeyed => (new Dictionary<string, string>() {
//             $allKeyedStr
//         }).AsReadOnly();

//         $listStr

//         private static string Decompress(string encoded)
//         {
//             var encodedBytes = Convert.FromBase64String(encoded);
//             using (var mem = new MemoryStream())
//             {
//                 mem.Write(encodedBytes, 0, encodedBytes.Length);

//                 mem.Position = 0;

//                 using (var gzip = new GZipStream(mem, CompressionMode.Decompress))
//                 using (var reader = new StreamReader(gzip))
//                 {
//                     return reader.ReadToEnd();
//                 }
//             }
//         }
//     }
// }
// EOF
// );

// if (\file_exists("../Tyhp/TyhpLang/Binder/PHPBuiltIn/Tyhpdef.cs")) {
//     \unlink("../Tyhp/TyhpLang/Binder/PHPBuiltIn/Tyhpdef.cs");
// }
// \rename("./PHP.Tyhpdef.cs", "../Tyhp/TyhpLang/Binder/PHPBuiltIn/Tyhpdef.cs");

// if (\file_exists("../Tyhp/TyhpLang/Binder/TyhpBuiltIn/Tyhpdef.cs")) {
//     \unlink("../Tyhp/TyhpLang/Binder/TyhpBuiltIn/Tyhpdef.cs");
// }
// \rename("./Tyhp.Tyhpdef.cs", "../Tyhp/TyhpLang/Binder/TyhpBuiltIn/Tyhpdef.cs");

class GenerateTyhpdef
{
    private const INCLUDE_PHP_EXAMPLES = false;

    protected static ?\DOMDocument $phpDocs = null;
    protected static ?\DOMXPath $xpath = null;
    protected static string $docLanguage = 'en';
    protected static ?\Closure $emit = null;
    protected static ?string $phpManualGZUrl = null;

    public const TYPE_GUARD_METHODS = [
        '\\is_array' => '{0} instanceof array',
        '\\is_bool' => '{0} instanceof bool',
        '\\is_callable' => '{0} instanceof callable',
        '\\is_countable' => '{0} instanceof \\Countable',
        '\\is_double' => '{0} instanceof float',
        '\\is_float' => '{0} instanceof float',
        '\\is_int' => '{0} instanceof int',
        '\\is_integer' => '{0} instanceof int',
        '\\is_iterable' => '{0} instanceof \\Traversable',
        '\\is_long' => '{0} instanceof int',
        '\\is_null' => '{0} instanceof null',
        '\\is_numeric' => '{0} instanceof int|float|string',
        '\\is_object' => '{0} instanceof object',
        '\\is_real' => '{0} instanceof float',
        '\\is_resource' => '{0} instanceof resource',
        '\\is_scalar' => '{0} instanceof int|float|string|bool',
        '\\is_string' => '{0} instanceof string',
    ];

    public static function resetDocs(): void
    {
        static::$phpDocs = null;
        static::$xpath = null;
        static::$docLanguage = 'en';
        static::$emit = null;
        static::$phpManualGZUrl = null;
    }

    public function process(string $extName, string $phpManualGZUrl, string $docLanguage, \Closure $emit): string
    {
        static::$emit = $emit;
        static::$phpManualGZUrl = $phpManualGZUrl;
        static::$docLanguage = $docLanguage;
        $result = static::run($extName, [], static::TYPE_GUARD_METHODS, true);

        $code = [];

        if (!empty($result['constants'])) {
            // $fn = $outputPath . "/_constants.tyhpdef";
            static::$emit?->__invoke('generating ' . $extName . ' CONSTANTS' . \PHP_EOL);
            // \file_put_contents($fn, $result['constants']);
            $code[] = $result['constants'];
        }

        if (!empty($result['functions'])) {
            // $fn = $outputPath . "/_functions.tyhpdef";
            static::$emit?->__invoke('generating ' . $extName . ' FUNCTIONS' . \PHP_EOL);
            // \file_put_contents($fn, $result['functions']);
            $code[] = $result['functions'];
        }

        if (!empty($result['objects'])) {
            foreach ($result['objects'] as $objName => $tyhpdefCode) {
                // $fn = $outputPath . "/" . $objName . ".tyhpdef";
                static::$emit?->__invoke('generating ' . $extName . '.' . $objName . ' OBJECT' . \PHP_EOL);
                // \file_put_contents($fn, $tyhpdefCode);
                $code[] = $tyhpdefCode;
            }
        }

        $extVersion = "*UNKNOWN*";

        try {
            $ext = new \ReflectionExtension($extName);
            $extVersion = $ext->getVersion();
        } catch (\ReflectionException) {
            // do nothing
        }

        $codeStr = "<?tyhpdef\n" .
            "/**\n * AUTO-GENERATED, DO NOT EDIT\n * Built using PHP v" . \PHP_VERSION . "\n * EXT: " . $extName . " v" . $extVersion . "\n */\n\n" .
            \implode("\n", $code);
        $codeStr = \trim($codeStr);

        // \file_put_contents($extName . ".tyhpdef", $codeStr);

        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        $codeStr = \str_replace("\n\n\n", "\n\n", $codeStr);
        
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);
        $codeStr = \str_replace("{\n\n", "{\n", $codeStr);

        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);
        $codeStr = \str_replace(";\n\n}", ";\n}", $codeStr);

        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        $codeStr = \str_replace(";\n\n    }", ";\n    }", $codeStr);
        
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);
        $codeStr = \preg_replace("/^ {4}/", "", $codeStr);

        // return \base64_encode(\gzcompress($codeStr, -1, \ZLIB_ENCODING_GZIP));
        return \base64_encode($codeStr);
    }

    public static function run(string $extName, array $namespaceAliases = [], array $typeGuardMethods = [], bool $loadDocsFromPHPNet = false): array
    {
        $rootAlias = $namespaceAliases[''] ?? '';
        if (empty($rootAlias)) {
            $rootAlias = '\\';
        } else {
            $rootAlias = '\\' . \trim($rootAlias, '\\') . '\\';
        }

        $result = [
            'constants' => '',
            'functions' => '',
            'objects' => [],
        ];

        if (!\extension_loaded($extName)) {
            return $result;
        }

        $ext = null;

        try {
            $ext = new \ReflectionExtension($extName);
        } catch (\ReflectionException) {
            return $result;
        }

        $tyhpdefHeader = "";
        $constNs = "";

        if (!empty($namespaceAliases[''])) {
            $constNs = 'namespace ' . $namespaceAliases[''] . "{\n\n";
        }

        // constants
        $items = [];
        $constants = $ext->getConstants();
        \ksort($constants);
        foreach ($constants as $name => $value) {
            if (\is_resource($value)) {
                $items[] = "const " . static::typeToCode($value) . " " . $name . ";";
            } else {
                // $items[] = "const " . static::typeToCode($value) . " " . $name . " ?? " . static::valueToCode($value) . ";";
                $items[] = "const " . static::typeToCode($value) . " " . $name . " = " . static::valueToCode($value) . ";";
            }
        }

        if (!empty($namespaceAliases[''])) {
            $constNs = "}\n";
        }

        if (!empty($items)) {
            $result['constants'] .= $tyhpdefHeader . $constNs;
            $result['constants'] .= \implode("\n", $items) . "\n";
        }

        // functions
        $items = [];
        $functions = $ext->getFunctions();
        \ksort($functions);
        foreach ($functions as $name => $value) {
            $ns = $value->getNamespaceName();
            if (!empty($namespaceAliases[$ns ?: ''])) {
                $ns = $namespaceAliases[$ns ?: ''];
            }
            $indent = '';
            if (!empty($ns)) {
                $indent = '    ';
            }


            $docData = [];
            if ($loadDocsFromPHPNet) {
                $docData = static::loadDocs('function', $name);
            }

            $docComment = $value->getDocComment();
            if (!empty($docComment)) {
                $docComment = \trim(\str_replace("\n", "\n" . $indent, $docComment)) . "\n" . $indent;
            }

            $pItems = [];
            $pDocItems = [];
            $pNames = [];
            foreach ($value->getParameters() as $pValue) {
                if ($pValue->isPromoted()) {
                    \dump(
                        [
                        'func' => $name,
                        'param_is_promoted' => $pValue->getName(),
                        ]
                    );
                }

                $pDefault = null;
                if ($pValue->isOptional() && !$pValue->isVariadic()) {
                    try {
                        $const = $pValue->getDefaultValueConstantName();
                        if (!\is_null($const)) {
                            $const = $rootAlias . $const;
                        }
                        $pDefault = $const ?? static::valueToCode($pValue->getDefaultValue());
                    } catch (\ReflectionException) {
                        $pDefault = 'null';
                    }
                }
                $pType = static::reflectionTypeToCode($pValue->getType(), $rootAlias);
                $pAttr = static::buildSingleLineAttributes(...$pValue->getAttributes());
                $pItems[] = (!empty($pAttr) ? $pAttr . " " : "") . $pType . " " . ($pValue->isPassedByReference() ? '&' : "") . ($pValue->isVariadic() ? '...' : '') . "\$" . $pValue->getName() . (!\is_null($pDefault) ? " = " . $pDefault : '');

                $pDocItems[] = \trim('@param ' . $pType . ($pValue->isVariadic() ? '[]' : '') . " \$" . $pValue->getName() . ' ' . (($docData['paramDoc'][$pValue->getName()] ?? '')));

                $pNames[] = "\$" . $pValue->getName();
            }

            $params = \implode(", ", $pItems);

            $returnRType = $value->getReturnType();
            $returnType = 'void';
            $docReturnType = $returnType;

            if (!\is_null($returnRType)) {
                $returnType = static::reflectionTypeToCode($returnRType, $rootAlias);
                $docReturnType = $returnType;

                $nsName = "\\" . ($ns ? $ns . "\\" : "") . $name;
                if ($returnType == 'bool' && \array_key_exists($nsName, $typeGuardMethods)) {
                    $returnType = $typeGuardMethods[$nsName];
                    foreach ($pNames as $idx => $pName) {
                        $returnType = \str_replace("{" . $idx . "}", $pName, $returnType);
                    }

                    $docReturnType = "bool\n" . $indent . " * @guard-return " . $returnType;
                }
            }

            if (empty($docComment)) {
                // lets build one
                $defaultOverview = "PHP function `" . $name . "`";
                $overview = $docData['overview'] ?? $defaultOverview;

                $docComment = "/**\n" . $indent . " * " . $overview . "\n" . $indent . "";
                $docComment .= " * \n" . $indent . "";

                if (!empty($docData['description'])) {
                    $docComment .= " * " . $docData['description'] . "\n" . $indent . "";
                    $docComment .= " * \n" . $indent . "";
                }

                if (!empty($docData['notes'])) {
                    foreach ($docData['notes'] as $note) {
                        $docComment .= " * " . $note . "\n" . $indent . "";
                        $docComment .= " * \n" . $indent . "";
                    }
                }

                if (!empty($docData['warnings'])) {
                    foreach ($docData['warnings'] as $warning) {
                        $docComment .= " * " . $warning . "\n" . $indent . "";
                        $docComment .= " * \n" . $indent . "";
                    }
                }

                if (!empty($docData['deprecated'])) {
                    foreach ($docData['deprecated'] as $deprecated) {
                        $docComment .= " * @deprecated " . $deprecated . "\n" . $indent . "";
                        $docComment .= " * \n" . $indent . "";
                    }
                }

                if (!empty($pDocItems)) {
                    $docComment .= " * " . \implode("\n" . $indent . " * ", $pDocItems) . "\n" . $indent . "";
                }
                $returnComment = \rtrim(' ' . ($docData['return'] ?? ''));

                $docComment .= " * @return " . $docReturnType . $returnComment . "\n" . $indent . "";


                // TODO: get throws info from php documentation and add @throws tags

                if (static::INCLUDE_PHP_EXAMPLES) {
                    foreach ($docData['examples'] ?? [] as $example) {
                        $docComment .= " *\n" . $indent . "";
                        $docComment .= " * ---\n" . $indent . "";
                        $docComment .= " *\n" . $indent . "";

                        // handle block comments in the example code:
                        // we will convert comments to single line "//" comments when comment block is only thing on lines
                        // we will convert to "//" comment when it is at the end of the line
                        // we will convert to "//" comment when it is at the start of the line, and move any code to next line
                        // we will convert to close/open php tags "?"."><?php// asdfasdf?"."><?php" when in middle of line

                        $inBlockComment = false;
                        foreach ($example as &$exampleLines) {
                            $exampleLines = \str_replace("\r\n", "\n", $exampleLines);
                            $exampleLines = \str_replace("\r", "\n", $exampleLines);
                            while (\str_contains($exampleLines, "/*")) {
                                $openPos = \strpos($exampleLines, '/*');
                                if ($openPos !== false) {
                                    $nextClosePos = \strpos($exampleLines, '*' . '/', $openPos);
                                    if ($nextClosePos === false) {
                                        $nextClosePos = \strlen($exampleLines);
                                    }
                                    $comment = \trim(\substr($exampleLines, $openPos + 2, ($nextClosePos - $openPos) - 2), "\n");
                                    $comment = \preg_replace("/\n(\\s*)/", "\$1// ", $comment);

                                    $isStartOfLine = false;
                                    if ($openPos > 0) {
                                        $prev = $openPos - 1;
                                        while ($prev >= 0 && \in_array($chr = \substr($exampleLines, $prev, 1), [ ' ', "\t", "\n" ])) {
                                            if ($chr === "\n") {
                                                $isStartOfLine = true;
                                                break;
                                            }
                                            $prev--;
                                        }
                                    } else {
                                        $isStartOfLine = true;
                                    }

                                    $isEndOfLine = false;
                                    if ($nextClosePos < \strlen($exampleLines) - 2) {
                                        $next = $nextClosePos + 2;
                                        while ($next < \strlen($exampleLines) && \in_array($chr = \substr($exampleLines, $next, 1), [ ' ', "\t", "\n" ])) {
                                            if ($chr === "\n") {
                                                $isEndOfLine = true;
                                                break;
                                            } else if ($next == \strlen($exampleLines) - 1) {
                                                // it really should never get here
                                                $nextClosePos = \strlen($exampleLines);
                                                $isEndOfLine = true;
                                                break;
                                            }
                                            $next++;
                                        }
                                    } else {
                                        $isEndOfLine = true;
                                    }

                                    if ($isEndOfLine) {
                                        // the comment sits on its own lines, so we can convert it many single line comments
                                        // or, the comment is at the end of the line, so we can convert it to a single line comment
                                        $exampleLines = \substr($exampleLines, 0, $openPos) . "// " . $comment . \substr($exampleLines, $nextClosePos + 2);
                                    } else if ($isStartOfLine) {
                                        // it is at the start of the line, so we will make it a single line comment on move the code down.
                                        $exampleLines = \substr($exampleLines, 0, $openPos) . "// " . $comment . "\n" . \substr($exampleLines, $nextClosePos + 2);
                                    } else {
                                        // it is in the middle of the line, so we need to convert it to single lines inside their own php tags
                                        $exampleLines = \substr($exampleLines, 0, $openPos) . "?><?php//" . $comment . "?><?php " . \substr($exampleLines, $nextClosePos + 2);
                                    }
                                } else {
                                    // we are unable to parse it, so skip this example
                                    break;
                                }
                            }
                        }

                        $inCode = false;
                        foreach ($example as $exampleLines) {
                            $eLines = \explode("\n", $exampleLines);
                            foreach ($eLines as $eLine) {
                                $isCodeFence = false;

                                if (\str_starts_with(\ltrim($eLine), '```')) {
                                    $inCode = !$inCode;
                                    $isCodeFence = true;
                                    if (!$inCode) {
                                        // we need to add a trailing space to make it render correctly for Intellephense.
                                        $eLine .= ' ';
                                    }
                                }

                                $docComment .= " *" . ($inCode && !$isCodeFence ? \mb_chr(0xfeff) . " " : " ") . $eLine . "\n" . $indent . "";
                            }
                        }
                    }

                    $docComment .= " * \n" . $indent . "";
                    $docComment .= " * --- \n" . $indent . "";
                    $docComment .= " * \n" . $indent . "";
                }

                if (!empty($docData['link'])) {
                    $docComment .= " * \n" . $indent . "";
                    $docComment .= " * @link " . $docData['link'] . "\n" . $indent . "";
                }
                $docComment .= " * \n" . $indent . "";
                $docComment .= " * @generated from PHP v" . \PHP_VERSION . ", EXT: " . $ext->getName() . " v" . $ext->getVersion() . "\n" . $indent . "";

                $docComment .= " */\n" . $indent . "";
            }

            $attributesStr = \implode("\n" . $indent, static::buildAttributes(...$value->getAttributes()));
            if (!empty($attributesStr)) {
                $attributesStr .= "\n" . $indent;
            }

            $items[$ns] = $items[$ns] ?? [];
            $items[$ns][] = $docComment . $attributesStr . ($value->isDeprecated() ? "deprecated " : "") . "function " . ($value->returnsReference() ? "&" : "") . $name . "(" . $params . "): " . $returnType . ";\n";
        }

        if (!empty($items)) {
            $result['functions'] .= $tyhpdefHeader;
            foreach ($items as $ns => $nsItems) {
                if (!empty($ns)) {
                    $result['functions'] .= "namespace " . $ns . "\n{\n";
                }

                $result['functions'] .= $indent . \implode("\n" . $indent, $nsItems);

                if (!empty($ns)) {
                    $result['functions'] .= "\n}";
                }
                $result['functions'] .= "\n";
            }
        }

        // objects
        $classes = static::sortReflectionItems(...$ext->getClasses());
        foreach ($classes as $class) {
            $docComment = $class->getDocComment();
            if (!empty($docComment)) {
                $docComment .= "\n";
            } else {
                // TODO get doc comment content from php documentation
            }

            $className = $class->getShortName();
            $ns = $class->getNamespaceName();
            if (!empty($namespaceAliases[$ns ?: ''])) {
                $ns = $namespaceAliases[$ns ?: ''];
            }
            $tyhpdefCode = $tyhpdefHeader;

            if (!empty($ns)) {
                $tyhpdefCode .= "namespace " . $ns . "{\n\n";
            }
            $tyhpdefCode .= $docComment;

            $attributesStr = \implode("\n", static::buildAttributes(...$class->getAttributes()));
            if (!empty($attributesStr)) {
                $attributesStr .= "\n";
            }

            $tyhpdefCode .= $attributesStr;

            if ($class->isInterface()) {
                $tyhpdefCode .= "interface " . $className;

                $extends = static::sortReflectionItems(...$class->getInterfaces());
                if (!empty($extends)) {
                    $tyhpdefCode .= " extends ";
                    $extendItems = [];
                    foreach ($extends as $extend) {
                        $extendItems[] = $rootAlias . $extend->getName();
                    }
                    $tyhpdefCode .= \implode(", ", $extendItems);
                }

                $tyhpdefCode .= "\n{\n";

                $methods = static::sortReflectionItems(...$class->getMethods());
                $methodItems = [];
                foreach ($methods as $method) {
                    $methodItems[] = static::methodToCode($method, $class, $typeGuardMethods, true, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $methodItems) . "\n";
                $tyhpdefCode .= "}\n";
            } else if ($class->isTrait()) {
                $tyhpdefCode .= "trait " . $className;
                $tyhpdefCode .= "\n{\n";

                $properties = static::sortReflectionItems(...$class->getProperties());
                $propertyItems = [];
                foreach ($properties as $property) {
                    $propertyItems[] = static::propertyToCode($property, $class, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $propertyItems) . "\n";

                $methods = static::sortReflectionItems(...$class->getMethods());
                $methodItems = [];
                foreach ($methods as $method) {
                    $methodItems[] = static::methodToCode($method, $class, $typeGuardMethods, true, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $methodItems) . "\n";
                $tyhpdefCode .= "}\n";
            } else if ($class->isEnum()) {
                $enum = ($class instanceof \ReflectionEnum) ? $class : new \ReflectionEnum($class->getName());

                $tyhpdefCode .= "enum " . $className;

                if ($enum->isBacked()) {
                    $backingType = static::reflectionTypeToCode($enum->getBackingType(), $rootAlias);
                    $tyhpdefCode .= ": " . $backingType;
                }

                if ($enum->getParentClass()) {
                    $tyhpdefCode .= " extends " . $rootAlias . $enum->getParentClass()->getName();
                }

                $implements = static::sortReflectionItems(...$class->getInterfaces());
                if (!empty($implements)) {
                    $tyhpdefCode .= " implements ";
                    $implementItems = [];
                    foreach ($implements as $implement) {
                        $implementItems[] = $rootAlias . $implement->getName();
                    }
                    $tyhpdefCode .= \implode(", ", $implementItems);
                }

                $tyhpdefCode .= "\n{\n";

                $cases = static::sortReflectionItems(...$enum->getCases());
                $caseItems = [];
                foreach ($cases as $case) {
                    $attributesStr = static::buildSingleLineAttributes(...$case->getAttributes());
                    if (!empty($attributesStr)) {
                        $attributesStr .= "\n    ";
                    }

                    $docComment = $case->getDocComment();
                    if (!empty($docComment)) {
                        $docComment .= "\n    ";
                    }
                    $caseVal = "";
                    if ($case instanceof \ReflectionEnumBackedCase) {
                        $caseVal = " = " . static::valueToCode($case->getBackingValue());
                    }

                    $caseItems[] = "    " . $docComment . $attributesStr . "case " . $case->getName() . $caseVal . ";\n";
                }

                $tyhpdefCode .= \implode("\n", $caseItems) . "\n";

                $constants = static::sortReflectionItems(...$class->getReflectionConstants());
                $constItems = [];
                foreach ($constants as $const) {
                    $constItems[] = static::classConstToCode($const, $class);
                }
                $tyhpdefCode .= \implode("\n", $constItems) . "\n";

                $properties = static::sortReflectionItems(...$class->getProperties());
                $propertyItems = [];
                foreach ($properties as $property) {
                    $propertyItems[] = static::propertyToCode($property, $class, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $propertyItems) . "\n";

                $methods = static::sortReflectionItems(...$class->getMethods());
                $methodItems = [];
                foreach ($methods as $method) {
                    $methodItems[] = static::methodToCode($method, $class, $typeGuardMethods, true, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $methodItems) . "\n";
                $tyhpdefCode .= "}\n";
            } else {
                $modifier = "";
                if ($class->isAbstract()) {
                    $modifier = "abstract ";
                } else if ($class->isFinal()) {
                    $modifier = "final ";
                }

                $tyhpdefCode .= "" . $modifier . "class " . $className;

                if ($class->getParentClass()) {
                    $tyhpdefCode .= " extends " . $rootAlias . $class->getParentClass()->getName();
                }

                $implements = static::sortReflectionItems(...$class->getInterfaces());
                if (!empty($implements)) {
                    $tyhpdefCode .= " implements ";
                    $implementItems = [];
                    foreach ($implements as $implement) {
                        $implementItems[] = $rootAlias . $implement->getName();
                    }
                    $tyhpdefCode .= \implode(", ", $implementItems);
                }

                $tyhpdefCode .= "\n{\n";

                $constants = static::sortReflectionItems(...$class->getReflectionConstants());
                $constItems = [];
                foreach ($constants as $const) {
                    $constItems[] = static::classConstToCode($const, $class);
                }
                $tyhpdefCode .= \implode("\n", $constItems) . "\n";

                $properties = static::sortReflectionItems(...$class->getProperties());
                $propertyItems = [];
                foreach ($properties as $property) {
                    $propertyItems[] = static::propertyToCode($property, $class, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $propertyItems) . "\n";

                $methods = static::sortReflectionItems(...$class->getMethods());
                $methodItems = [];
                foreach ($methods as $method) {
                    $methodItems[] = static::methodToCode($method, $class, $typeGuardMethods, true, $rootAlias);
                }

                $tyhpdefCode .= \implode("\n", $methodItems) . "\n";
                $tyhpdefCode .= "}\n";
            }

            if (!empty($ns)) {
                $tyhpdefCode .= "}\n";
            }

            if (!empty($tyhpdefCode)) {
                $result['objects'][$class->getShortName()] = $tyhpdefCode . "\n";
            }
        }

        return $result;
    }

    protected static function getAttributeConst(int $flags): string
    {
        $result = [];
        if ($flags === \Attribute::TARGET_ALL || $flags === (\Attribute::IS_REPEATABLE | \Attribute::TARGET_ALL)) {
            $result[] = "Attribute::TARGET_ALL";
        } else {
            if ($flags & \Attribute::TARGET_CLASS) {
                $result[] = "Attribute::TARGET_CLASS";
            }
            if ($flags & \Attribute::TARGET_METHOD) {
                $result[] = "Attribute::TARGET_METHOD";
            }
            if ($flags & \Attribute::TARGET_FUNCTION) {
                $result[] = "Attribute::TARGET_FUNCTION";
            }
            if ($flags & \Attribute::TARGET_PROPERTY) {
                $result[] = "Attribute::TARGET_PROPERTY";
            }
            if ($flags & \Attribute::TARGET_PARAMETER) {
                $result[] = "Attribute::TARGET_PARAMETER";
            }
            if ($flags & \Attribute::TARGET_CLASS_CONSTANT) {
                $result[] = "Attribute::TARGET_CLASS_CONSTANT";
            }
        }
        if ($flags & \Attribute::IS_REPEATABLE) {
            $result[] = "Attribute::IS_REPEATABLE";
        }
        return \implode("|", $result);
    }

    protected static function attributeArgumentToCode(mixed $value, string $type): string
    {
        if ($type === "Attribute" && \is_int($value)) {
            return static::getAttributeConst($value);
        }
        return static::valueToCode($value);
    }

    protected static function buildSingleLineAttributes(\ReflectionAttribute ...$items): string
    {
        if (empty($items)) {
            return "";
        }

        $list = [];
        foreach (static::sortReflectionItems(...$items) as $item) {
            $list[] = $item->getName() . "(" . \implode(", ", \array_map(fn($a) => static::attributeArgumentToCode($a, $item->getName()), $item->getArguments())) . ")";
        }
        return "#[" . \implode(", ", $list) . "]";
    }

    protected static function buildAttributes(\ReflectionAttribute ...$items): array
    {
        if (empty($items)) {
            return [];
        }

        $result = [];
        foreach (static::sortReflectionItems(...$items) as $item) {
            $result[] = "#[" . $item->getName() . "(" . \implode(", ", \array_map(fn($a) => static::attributeArgumentToCode($a, $item->getName()), $item->getArguments())) . ")]";
        }
        return $result;
    }

    protected static function sortReflectionItems(\ReflectionEnumUnitCase|\ReflectionProperty|\ReflectionMethod|\ReflectionClass|\ReflectionClassConstant|\ReflectionAttribute ...$items): array
    {
        \usort($items, fn($a, $b) => $a->getName() <=> $b->getName());
        return $items;
    }

    protected static function loadDocs(string $type, string $name, ?string $subname = null): array
    {
        if (\is_null(static::$phpDocs)) {
            $htmlDocs = \file_get_contents(static::$phpManualGZUrl);
            $htmlDocs = \gzdecode($htmlDocs);
            \libxml_use_internal_errors(true);
            \libxml_clear_errors();
            static::$phpDocs = new \DOMDocument();
            static::$phpDocs->recover = true;
            static::$phpDocs->loadHTML('<?xml encoding="UTF-8">' . $htmlDocs, \LIBXML_NOWARNING | \LIBXML_PARSEHUGE);
        }

        if (\is_null(static::$xpath)) {
            static::$xpath = new \DOMXPath(static::$phpDocs, false);
        }

        $docData = [
            'overview' => '',
            'description' => '',
            'phpVersions' => [],
            'notes' => [],
            'warnings' => [],
            'deprecated' => [],
            'paramNotes' => '',
            'paramDoc' => [],
            'return' => '',
            'examples' => [],
            'throws' => [], // TODO
            'link' => '',
        ];

        switch ($type) {
            case 'function':
                $xpathPrefix = '.';
                $idName = \str_replace("_", "-", $name);
                $docData['link'] = "https://www.php.net/manual/" . static::$docLanguage . "/function." . $idName . ".php" ;

                $functionNode = static::$phpDocs->getElementById("function." . $idName);

                $docData['overview'] = static::normalizeText(static::phpDocHtmlNodeToMarkdown(static::$xpath->evaluate("(" . $xpathPrefix . "/div[@class='refnamediv']/p[@class='refpurpose']/span[@class='dc-title'])[1]", $functionNode, false)));
                $docData['description'] = static::normalizeText(static::phpDocHtmlNodeToMarkdown(static::$xpath->evaluate("(" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-description']/p[contains(@class, 'rdfs-comment')])[1]", $functionNode, false)));
                $docData['phpVersions'] = static::$xpath->evaluate("string(" . $xpathPrefix . "/div[@class='refnamediv']/p[@class='verinfo']/text())", $functionNode, false);
                $docData['phpVersions'] = \array_map(fn($pv) => static::normalizeText($pv), \explode(",", \trim(\trim($docData['phpVersions'], '()'))));
                $docData['notes'] = static::phpDocHtmlToMarkdown(static::$xpath->evaluate("" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-description']/blockquote[@class='note']", $functionNode, false));
                $docData['warnings'] = static::phpDocHtmlToMarkdown(static::$xpath->evaluate("" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-description']/div[@class='caution']", $functionNode, false));
                $docData['deprecated'] = static::phpDocHtmlToMarkdown(static::$xpath->evaluate("" . $xpathPrefix . "/div[@id='function." . $idName . "-refsynopsisdiv']/div[@class='warning']", $functionNode, false));
                /** @var \DOMNodeList $paramNode */
                $paramNode = static::$xpath->evaluate("(" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-parameters']/p[@class='para'])[1]", $functionNode, false);
                $paramNode = ($paramNode->count() > 0) ? $paramNode->item(0) : null;
                /** @var \DOMNode|null $paramNode */
                if (!\is_null($paramNode)) {
                    foreach ($paramNode->childNodes as $cNode) {
                        // \dump($cNode->getNodePath());
                        if ($cNode->nodeType == \XML_TEXT_NODE) {
                            $docData['paramNotes'] .= " " . static::normalizeText($cNode->nodeValue);
                            $docData['paramNotes'] = static::normalizeText($docData['paramNotes']);
                        } else if ($cNode->nodeName == 'dl') {
                            $key = '';
                            foreach ($cNode->childNodes as $lNode) {
                                if ($lNode->nodeName == 'dt') {
                                    $key = static::normalizeText(\trim(static::phpDocHtmlNodeToMarkdown($lNode), '`'));
                                } else if ($lNode->nodeName == 'dd') {
                                    $docData['paramDoc'][$key] = static::normalizeText(static::phpDocHtmlNodeToMarkdown($lNode));
                                }
                            }
                        } else {
                            $docData['paramNotes'] .= " " . static::phpDocHtmlNodeToMarkdown($cNode);
                            $docData['paramNotes'] = static::normalizeText($docData['paramNotes']);
                        }
                    }
                }
                // sometimes there is a bug reading the html that puts the `dl` tag outside the `p` tag.
                if (empty($docData['paramDoc'])) {
                    $paramNodes = static::$xpath->evaluate("" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-parameters']", $functionNode, false);
                    foreach ($paramNodes as $pNode) {
                        if ($pNode->nodeName == 'div') {
                            foreach ($pNode->childNodes as $cNode) {
                                if ($cNode->nodeName == 'dl') {
                                    $key = '';
                                    foreach ($cNode->childNodes as $lNode) {
                                        if ($lNode->nodeName == 'dt') {
                                            $key = static::normalizeText(\trim(\trim(static::phpDocHtmlNodeToMarkdown($lNode)), '`$'));
                                        } else if ($lNode->nodeName == 'dd') {
                                            $docData['paramDoc'][$key] = static::normalizeText(static::phpDocHtmlNodeToMarkdown($lNode));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                $retValNode = static::$xpath->evaluate("(" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-returnvalues']/p[@class='para'])[1]", $functionNode, false);
                $retValNode = ($retValNode->count() > 0) ? $retValNode->item(0) : null;
                /** @var \DOMNode|null $retValNode */
                if (!\is_null($retValNode)) {
                    foreach ($retValNode->childNodes as $cNode) {
                        $docData['return'] .= " " . static::phpDocHtmlNodeToMarkdown($cNode);
                        $docData['return'] = static::normalizeText($docData['return']);
                    }
                }
                if (static::INCLUDE_PHP_EXAMPLES) {
                    $exampleNodes = static::$xpath->evaluate("" . $xpathPrefix . "/div[@id='refsect1-function." . $idName . "-examples']/div[@class='example']", $functionNode, false);
                    foreach ($exampleNodes as $eNode) {
                        $example = [];
                        foreach ($eNode->childNodes as $cNode) {
                            $eResult = \trim(static::phpDocHtmlNodeToMarkdown($cNode));
                            if (!empty($eResult)) {
                                $example[] = \str_replace(['&nbsp;', ' '], [' ', ' '], $eResult);
                            }
                        }
                        $docData['examples'][] = $example;
                    }
                }

                break;
        }
        // if (empty($docData['overview'])) {
        //     \dump([
        //         'name' => $name,
        //         'id' => "function." . $idName,
        //         'html' =>$functionNode?->ownerDocument->saveHTML($functionNode)
        //     ]);
        // }
        // \dump($docData);
        return $docData;
    }

    protected static function phpDocHtmlToMarkdown(\DOMNodeList $nodes): array
    {
        $result = [];
        foreach ($nodes as $node) {
            /** @var \DOMNode $node */
            $result[] = static::normalizeText(static::phpDocHtmlNodeToMarkdown($node));
        }
        return $result;
    }

    protected static function phpDocHtmlNodeToMarkdown(\DOMNode|\DOMNodeList $node, bool $normalizeTextNodes = true, bool $skipCodeTagFormatting = false, bool $inCodeBlock = false): string
    {
        if ($node instanceof \DOMNodeList) {
            foreach ($node as $n) {
                $node = $n;
                break;
            }
            if ($node instanceof \DOMNodeList) {
                // it was an empty list so return a blank string
                return '';
            }
        }

        $result = " ";
        $closeText = '';
        if ($node->nodeName == 'code' && !$skipCodeTagFormatting && \str_contains($node->attributes?->getNamedItem("class")?->nodeValue ?? '', 'parameter') && !$inCodeBlock) {
            $result .= '`$';
            $closeText = "`";
            $inCodeBlock = true;
        } else if (
            (
                ($node->nodeName == 'span' && \str_contains($node->attributes?->getNamedItem("class")?->nodeValue ?? '', 'function')) ||
                ($node->nodeName == 'code' && !$skipCodeTagFormatting)
            ) && !$inCodeBlock
        ) {
            $result .= "`";
            $closeText = "`";
            $inCodeBlock = true;
        } else if (
            ($node->nodeName == 'div' && \str_contains($node->attributes?->getNamedItem("class")?->nodeValue ?? '', 'phpcode')) &&
            !$inCodeBlock
        ) {
            $result .= "```php\n";
            $closeText = "\n```";
            $normalizeTextNodes = false;
            $skipCodeTagFormatting = true;
            $inCodeBlock = true;
        } else if (
            ($node->nodeName == 'div' && \str_contains($node->attributes?->getNamedItem("class")?->nodeValue ?? '', 'screen')) &&
            !$inCodeBlock
        ) {
            $result .= "```plaintext\n";
            $closeText = "\n```";
            $normalizeTextNodes = false;
            $skipCodeTagFormatting = true;
            $inCodeBlock = true;
        } else if ($node->nodeName == 'pre' && !$inCodeBlock) {
            $result .= "```plaintext\n";
            $closeText = "\n```";
            $normalizeTextNodes = false;
            $skipCodeTagFormatting = true;
            $inCodeBlock = true;
        } else if ($node->nodeName == 'strong' && !$inCodeBlock) {
            $result .= "**";
            $closeText = "**";
        } else if ($node->nodeName == 'em' && !$inCodeBlock) {
            $result .= "*";
            $closeText = "*";
        } else if ($node->nodeName == 'br') {
            $result .= "\n";
        } else if ($node->nodeType == \XML_TEXT_NODE) {
            if ($normalizeTextNodes) {
                $result .= static::normalizeText($node->nodeValue);
            } else {
                $result .= \str_replace('&nbsp;', ' ', $node->nodeValue);
            }
        } else {
            // do nothing directly
        }
        // } else {
        //     $result .= '<' . $node->nodeName . ' type="' . $node->nodeType . '">';
        //     $closeText = '</' . $node->nodeName . '>';
        // }

        $first = true;
        foreach ($node->childNodes as $cNode) {
            $cResult = static::phpDocHtmlNodeToMarkdown($cNode, $normalizeTextNodes, $skipCodeTagFormatting, $inCodeBlock);
            if ($first) {
                $cResult = \ltrim($cResult);
                $first = false;
            }
            $result .= $cResult;
        }

        if (!empty($closeText)) {
            $result = \rtrim($result);
            $closeText .= " ";
        }

        $result = \str_replace('*/', '* /', $result);

        return $result . $closeText;
    }

    protected static function normalizeText(string $text): string
    {
        $text = \str_replace('&nbsp;', ' ', $text);
        $text = \trim($text);
        $text = \str_replace(["\n", "\r", "\t"], [' ', '', ' '], $text);
        while (\str_contains($text, '  ')) {
            $text = \str_replace('  ', ' ', $text);
        }

        $text = \str_replace(
            [
                ' .',
                ' !',
                ' ?',
                '( ',
                ' )',
                '[ ',
                ' ]',
                '{ ',
                ' }',
            ],
            [
                '.',
                '!',
                '?',
                '(',
                ')',
                '[',
                ']',
                '{',
                '}',
            ],
            $text
        );

        $text = \html_entity_decode($text);

        return $text;
    }

    protected static function typeToCode($value): string
    {
        if (\is_array($value)) {
            return "array";
        } else if (\is_bool($value)) {
            return "bool";
        } else if (\is_float($value)) {
            return "float";
        } else if (\is_int($value)) {
            return "int";
        } else if (\is_null($value)) {
            return "null";
        } else if (\is_string($value)) {
            return "string";
        } else if (\is_resource($value)) {
            return "resource";
        } else if (\is_object($value)) {
            return "object";
        } else {
            static::$emit?->__invoke("Unexpected type, got: " . \gettype($value));
            throw new \Exception("Unexpected type, got: " . \gettype($value));
        }
    }

    protected static function reflectionTypeToCode(\ReflectionType|null $rType, string $rootAlias): string
    {
        $type = "mixed";

        if (!\is_null($rType)) {
            if ($rType instanceof \ReflectionUnionType) {
                $tItems = [];
                foreach ($rType->getTypes() as $uType) {
                    $tItems[] = (static::typeIsBuiltIn($uType) ? '' : $rootAlias) . $uType->getName();
                }
                $type = \implode('|', $tItems);
            } else if ($rType instanceof \ReflectionIntersectionType) {
                $tItems = [];
                foreach ($rType->getTypes() as $uType) {
                    if ($uType instanceof \ReflectionNamedType) {
                        $tItems[] = (static::typeIsBuiltIn($uType) ? '' : $rootAlias) . $uType->getName();
                    } else if ($uType instanceof \ReflectionUnionType) {
                        $subTItems = [];
                        foreach ($uType->getTypes() as $subUType) {
                            $subTItems[] = (static::typeIsBuiltIn($subUType) ? '' : $rootAlias) . $subUType->getName();
                        }
                        $tItems[] = \implode('|', $subTItems);
                    } else {
                        \dd(
                            [
                            'OTHER TYPE' => true,
                            'type' => \get_class($uType),
                            ]
                        );
                    }
                }
                $type = \implode('&', $tItems);
            } else if ($rType instanceof \ReflectionNamedType) {
                $type = (static::typeIsBuiltIn($rType) ? '' : $rootAlias) . $rType->getName();
            } else {
                $type = $rType->__toString() ?: 'mixed';
            }

            if ($rType->allowsNull() && $type !== 'mixed') {
                if (\strpos($type, '|') !== false || \strpos($type, '&') !== false) {
                    if (!\preg_match("/\\bnull\\b/", $type)) {
                        $type .= "|null";
                    }
                } else {
                    $type = '?' . $type;
                }
            }
        }

        return $type;
    }

    protected static function typeIsBuiltIn(\ReflectionNamedType $type): bool
    {
        return \in_array($type->getName(), [ 'int', 'float', 'string', 'bool', 'array', 'true', 'false', 'null', 'static', 'self', 'void', "mixed", "callable", "object" ]);
    }

    protected static function valueToCode($value): string
    {
        if (\is_array($value)) {
            return static::arrayToCode($value);
        } else if (\is_bool($value)) {
            return $value ? "true" : "false";
        } else if (\is_float($value)) {
            return \strval($value);
        } else if (\is_int($value)) {
            return \strval($value);
        } else if (\is_null($value)) {
            return "null";
        } else if (\is_string($value)) {
            return '"' . \addcslashes($value, "\0\r\n\f\v\t\\\"") . '"';
        } else {
            static::$emit?->__invoke("Unexpected type, got: " . \gettype($value));
        }
    }

    /**
     * PHP var_export() with short array syntax (square brackets) indented 2 spaces.
     *
     * NOTE: The only issue is when a string value has `=>\n[`, it will get converted to `=> [`
     * @link https://www.php.net/manual/en/function.var-export.php#124194
     */
    protected static function arrayToCode($expression)
    {
        $export = \var_export($expression, true);
        $patterns = [
            "/array \(/" => '[',
            "/^([ ]*)\)(,?)$/m" => '$1]$2',
            "/=>[ ]?\n[ ]+\[/" => '=> [',
            "/([ ]*)(\'[^\']+\') => ([\[\'])/" => '$1$2 => $3',
        ];
        return \str_replace("[\n]", "[]", \preg_replace(\array_keys($patterns), \array_values($patterns), $export));
    }

    protected static function classConstToCode(\ReflectionClassConstant $const, \ReflectionClass $class): string
    {
        $attributesStr = \implode("\n    ", static::buildAttributes(...$const->getAttributes()));
        if (!empty($attributesStr)) {
            $attributesStr .= "\n    ";
        }

        $type = static::typeToCode($const->getValue());

        $docComment = $const->getDocComment();
        if (!empty($docComment)) {
            $docComment .= "\n    ";
        }

        $modifierItems = [];
        if ($const->isPublic()) {
            $modifierItems[] = 'public';
        } else if ($const->isProtected()) {
            $modifierItems[] = 'protected';
        } else if ($const->isPrivate()) {
            $modifierItems[] = 'private';
        }

        $value = "";
        if (!\is_object($const->getValue()) && !\is_resource($const->getValue())) {
            $value = " = " . static::valueToCode($const->getValue());
        }

        return "    " . $docComment . $attributesStr .\implode(" ", $modifierItems) . " const " . $type . " " . $const->getName() . $value . ";\n";
    }

    protected static function propertyToCode(\ReflectionProperty $property, \ReflectionClass $class, string $rootAlias): string
    {
        $attributesStr = \implode("\n    ", static::buildAttributes(...$property->getAttributes()));
        if (!empty($attributesStr)) {
            $attributesStr .= "\n    ";
        }

        $type = static::reflectionTypeToCode($property->getType(), $rootAlias);

        $docComment = $property->getDocComment();
        if (!empty($docComment)) {
            $docComment .= "\n    ";
        } else {
            $docComment = "/** @var " . $type . " */\n    ";
        }

        $defaultValue = "";
        // if (!empty($property->getDefaultValue())) {
        //     $defaultValue = " = " . static::valueToCode($property->getDefaultValue());
        // }

        $modifierItems = [];
        if ($property->isPublic()) {
            $modifierItems[] = 'public';
        } else if ($property->isProtected()) {
            $modifierItems[] = 'protected';
        } else if ($property->isPrivate()) {
            $modifierItems[] = 'private';
        }

        if ($property->isStatic()) {
            $modifierItems[] = 'static';
        }

        if ($property->isReadonly()) {
            $modifierItems[] = 'readonly';
        }

        return "    " . $docComment . $attributesStr .\implode(" ", $modifierItems) . " " . $type . " $" . $property->getName() . $defaultValue . ";\n";
    }

    protected static function methodToCode(\ReflectionMethod $method, \ReflectionClass $class, array $typeGuardMethods = [], bool $forInterface = false, string $rootAlias = '\\'): string
    {
        $attributesStr = \implode("\n    ", static::buildAttributes(...$method->getAttributes()));
        if (!empty($attributesStr)) {
            $attributesStr .= "\n    ";
        }

        $name = $method->getName();
        $docComment = $method->getDocComment();
        if (!empty($docComment)) {
            $docComment = \trim($docComment) . "\n    ";
        }

        $pItems = [];
        $pDocItems = [];
        $pNames = [];
        foreach ($method->getParameters() as $pValue) {
            if ($pValue->isPromoted()) {
                \dump(
                    [
                    'method' => $name,
                    'param_is_promoted' => $pValue->getName(),
                    ]
                );
            }

            $pDefault = null;
            if ($pValue->isOptional() && !$pValue->isVariadic()) {
                try {
                    $const = $pValue->getDefaultValueConstantName();
                    if (!\is_null($const)) {
                        $const = $rootAlias . $const;
                    }
                    $pDefault = $const ?? static::valueToCode($pValue->getDefaultValue());
                } catch (\ReflectionException) {
                    $pDefault = 'null';
                }
            }
            $pType = static::reflectionTypeToCode($pValue->getType(), $rootAlias);

            $pAttr = static::buildSingleLineAttributes(...$pValue->getAttributes());
            if (!empty($pAttr)) {
                $pAttr .= " ";
            }

            $pItems[] = $pAttr . $pType . " " . ($pValue->isPassedByReference() ? '&' : "") . ($pValue->isVariadic() ? '...' : '') . "\$" . $pValue->getName() . (!\is_null($pDefault) ? " = " . $pDefault : '');

            // TODO: get php doc parameter help text from php documentation
            $pDocItems[] = '@param ' . $pType . ($pValue->isVariadic() ? '[]' : '') . " \$" . $pValue->getName();

            $pNames[] = "\$" . $pValue->getName();
        }

        $params = \implode(", ", $pItems);
        $returnRType = $method->getReturnType() ?? $method->getTentativeReturnType();
        $returnType = 'void';
        $docReturnType = $returnType;

        if (!\is_null($returnRType)) {
            $returnType = static::reflectionTypeToCode($returnRType, $rootAlias);
            $docReturnType = $returnType;

            $nsName = $rootAlias . $class->getName() . "::" . $name;
            if ($returnType == 'bool' && \array_key_exists($nsName, $typeGuardMethods)) {
                $returnType = $typeGuardMethods[$nsName];
                foreach ($pNames as $idx => $pName) {
                    $returnType = \str_replace("{" . $idx . "}", $pName, $returnType);
                }

                $docReturnType = "bool\n     * @guard-return " . $returnType;
            }
        }

        if (empty($docComment)) {
            // lets build one
            // TODO get PHP doc "purpose" string from php documentation
            $docComment = "/**\n     * `" . $rootAlias . $class->getName() . "` method `" . $name . "`\n    ";
            $docCommentSkipLine = " *\n    ";
            $skippedLine = false;
            if (!empty($pDocItems)) {
                if (!$skippedLine) {
                    $docComment .= $docCommentSkipLine;
                    $skippedLine = true;
                }
                $docComment .= " * " . \implode("\n     * ", $pDocItems) . "\n    ";
            }
            if (!\in_array($name, ['__construct', '__destruct'])) {
                if (!$skippedLine) {
                    $docComment .= $docCommentSkipLine;
                    $skippedLine = true;
                }
                $docComment .= " * @return " . $docReturnType . "\n    ";
            }

            if ($method->isDeprecated()) {
                if (!$skippedLine) {
                    $docComment .= $docCommentSkipLine;
                    $skippedLine = true;
                }
                $docComment .= " *\n     * @deprecated";
            }

            // TODO: get throws info from php documentation and ad @throws tags

            $docComment .= " */\n    ";
        }

        $modifierItems = [];
        if (!$forInterface) {
            if ($method->isAbstract()) {
                $modifierItems[] = 'abstract';
            } else if ($method->isFinal()) {
                $modifierItems[] = 'final';
            }
            if ($method->isPublic()) {
                $modifierItems[] = 'public';
            } else if ($method->isProtected()) {
                $modifierItems[] = 'protected';
            } else if ($method->isPrivate()) {
                $modifierItems[] = 'private';
            } else {
                // default
                $modifierItems[] = 'public';
            }
            if ($method->isStatic()) {
                $modifierItems[] = 'static';
            }
        } else {
            $modifierItems[] = 'public';
        }

        return "    " . $docComment . $attributesStr . ($method->isDeprecated() ? "deprecated " : "") . \implode(" ", $modifierItems) . " function " . ($method->returnsReference() ? "&" : "") . $name . "(" . $params . ")". (!\in_array($name, ['__construct', '__destruct']) ? ": " . $returnType : "") . ";\n";
    }
}
