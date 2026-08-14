#!/usr/bin/env php
<?php

/**
 * One-shot converter: docs/content/*.json page files → Markdown + YAML front matter.
 *
 * Usage: php tools/convert_json_to_md.php
 */

declare(strict_types=1);

\chdir(\dirname(__DIR__));

require __DIR__ . '/../vendor/autoload.php';

use Symfony\Component\Yaml\Yaml;

const RED = "\033[0;31m";
const GREEN = "\033[0;32m";
const YELLOW = "\033[1;33m";
const NC = "\033[0m";

function echoStd(string $text): void
{
    \fwrite(\STDOUT, $text . "\n");
}

function echoWarn(string $text): void
{
    \fwrite(\STDERR, YELLOW . $text . NC . "\n");
}

function echoErr(string $text): void
{
    \fwrite(\STDERR, RED . $text . NC . "\n");
}

/**
 * Choose a fence marker longer than any run of backticks in $content.
 */
function chooseFence(string $content, string $info = ''): array
{
    $maxRun = 2;
    if (\preg_match_all('/`+/', $content, $matches) > 0) {
        foreach ($matches[0] as $run) {
            $len = \strlen($run);
            if ($len > $maxRun) {
                $maxRun = $len;
            }
        }
    }
    $fence = \str_repeat('`', $maxRun + 1);
    $open = $info === '' ? $fence : $fence . $info;

    return [$open, $fence];
}

function dumpYaml(mixed $data): string
{
    return \trim(Yaml::dump($data, 8, 2, Yaml::DUMP_MULTI_LINE_LITERAL_BLOCK));
}

/**
 * Convert trivial HTML inline tags to Markdown when safe.
 */
function htmlToMarkdownHints(string $text, bool $allowHtml): string
{
    if (!$allowHtml) {
        return $text;
    }

    // <code>...</code> → `...` when no backticks inside
    $text = \preg_replace_callback(
        '/<code>([^<]*)<\/code>/i',
        static function (array $m): string {
            if (\str_contains($m[1], '`')) {
                return $m[0];
            }
            $inner = \html_entity_decode($m[1], \ENT_QUOTES | \ENT_HTML5, 'UTF-8');

            return '`' . $inner . '`';
        },
        $text
    ) ?? $text;

    // <b>...</b> / <strong>...</strong> → **...** when no * inside
    $text = \preg_replace_callback(
        '/<(b|strong)>([^<]*)<\/\1>/i',
        static function (array $m): string {
            if (\str_contains($m[2], '*')) {
                return $m[0];
            }
            $inner = \html_entity_decode($m[2], \ENT_QUOTES | \ENT_HTML5, 'UTF-8');

            return '**' . $inner . '**';
        },
        $text
    ) ?? $text;

    // Decode remaining entities outside tags (e.g. &lt; in prose)
    $text = \html_entity_decode($text, \ENT_QUOTES | \ENT_HTML5, 'UTF-8');

    return $text;
}

function escapeMdParagraph(string $text, bool $allowHtml): string
{
    $text = htmlToMarkdownHints($text, $allowHtml);
    if (!$allowHtml) {
        // leave as plain text; backticks for inline code already in source as `...` sometimes
        return $text;
    }

    return $text;
}

function convertElements(array $elements, string $sourceFile): string
{
    $parts = [];

    foreach ($elements as $el) {
        if (!\is_array($el)) {
            continue;
        }

        $type = $el['type'] ?? '';
        $allowHtml = (bool) ($el['allowHtml'] ?? false);

        switch ($type) {
            case 'text':
                $content = escapeMdParagraph((string) ($el['content'] ?? ''), $allowHtml);
                $parts[] = $content;
                $parts[] = '';
                break;

            case 'header':
                $content = escapeMdParagraph((string) ($el['content'] ?? ''), $allowHtml);
                $parts[] = '## ' . $content;
                $parts[] = '';
                break;

            case 'note':
            case 'tip':
            case 'warning':
            case 'danger':
                $title = \trim((string) ($el['title'] ?? ''));
                $opener = ':::' . $type;
                if ($title !== '') {
                    $opener .= ' ' . $title;
                }
                $content = escapeMdParagraph((string) ($el['content'] ?? ''), $allowHtml);
                $parts[] = $opener;
                $parts[] = $content;
                $parts[] = ':::';
                $parts[] = '';
                break;

            case 'alert':
                $style = \trim((string) ($el['style'] ?? 'primary'));
                if ($style === '') {
                    $style = 'primary';
                }
                $opener = ':::alert{style=' . $style . '}';
                $content = escapeMdParagraph((string) ($el['content'] ?? ''), $allowHtml);
                $parts[] = $opener;
                $parts[] = $content;
                $parts[] = ':::';
                $parts[] = '';
                break;

            case 'bulletList':
            case 'list': // alias used in some pages
                foreach ($el['content'] ?? [] as $item) {
                    $itemText = escapeMdParagraph((string) $item, $allowHtml);
                    $parts[] = '- ' . $itemText;
                }
                $parts[] = '';
                break;

            case 'numList':
                $n = 1;
                foreach ($el['content'] ?? [] as $item) {
                    $itemText = escapeMdParagraph((string) $item, $allowHtml);
                    $parts[] = $n . '. ' . $itemText;
                    $n++;
                }
                $parts[] = '';
                break;

            case 'tyhpCode':
            case 'phpCode':
            case 'jsonCode':
            case 'codeBlock':
                $lang = match ($type) {
                    'tyhpCode' => 'tyhp',
                    'phpCode' => 'php',
                    'jsonCode' => 'json',
                    default => '',
                };
                $codeLines = $el['content'] ?? [];
                if (!\is_array($codeLines)) {
                    $codeLines = [(string) $codeLines];
                }
                $code = \implode("\n", $codeLines);
                [$open, $close] = chooseFence($code, $lang);
                $parts[] = $open;
                $parts[] = $code;
                $parts[] = $close;
                $parts[] = '';
                break;

            case 'tyhpCodeFile':
            case 'phpCodeFile':
            case 'jsonCodeFile':
                echoWarn("Skipping deferred *CodeFile in {$sourceFile}: type={$type} source=" . ($el['source'] ?? ''));
                break;

            case 'includeContent':
                echoWarn("Skipping deferred includeContent in {$sourceFile}: source=" . ($el['source'] ?? ''));
                break;

            case 'statusBadge':
                $status = $el['status'] ?? $el;
                if (\is_array($status)) {
                    unset($status['type']);
                }
                $yaml = dumpYaml($status);
                [$open, $close] = chooseFence($yaml, 'status');
                $parts[] = $open;
                $parts[] = $yaml;
                $parts[] = $close;
                $parts[] = '';
                break;

            case 'classDef':
                $yaml = dumpYaml($el['content'] ?? []);
                [$open, $close] = chooseFence($yaml, 'classdef');
                $parts[] = $open;
                $parts[] = $yaml;
                $parts[] = $close;
                $parts[] = '';
                break;

            case 'functionDef':
                $yaml = dumpYaml($el['content'] ?? []);
                [$open, $close] = chooseFence($yaml, 'functiondef');
                $parts[] = $open;
                $parts[] = $yaml;
                $parts[] = $close;
                $parts[] = '';
                break;

            case 'memberDescription':
                $identifier = (string) ($el['content']['identifier'] ?? '');
                $description = (string) ($el['content']['description'] ?? '');
                // Raw HTML in member descriptions is allowed in MD (html_input allow).
                // Convert trivial tags when allowHtml; otherwise leave as-is / convert if HTML present.
                if ($allowHtml) {
                    $description = htmlToMarkdownHints($description, true);
                } else {
                    // Descriptions often contain HTML even when allowHtml is false/omitted
                    // (diagnostics_reference). Keep HTML as-is so CommonMark can pass it through.
                    $description = htmlToMarkdownHints($description, true);
                }
                $parts[] = ':::member[' . $identifier . ']';
                $parts[] = $description;
                $parts[] = ':::';
                $parts[] = '';
                break;

            default:
                echoWarn("Unknown element type \"{$type}\" in {$sourceFile} — skipped");
                break;
        }
    }

    return \rtrim(\implode("\n", $parts)) . "\n";
}

function convertPage(array $json, string $sourceFile): string
{
    $front = [];
    $front['title'] = (string) ($json['title'] ?? '');
    if (!empty($json['status']) && \is_array($json['status'])) {
        $front['status'] = $json['status'];
    }

    $yaml = \trim(Yaml::dump($front, 8, 2));
    $body = convertElements($json['content'] ?? [], $sourceFile);

    return "---\n{$yaml}\n---\n\n{$body}";
}

// --- main ---

$contentDir = __DIR__ . '/../content';
$files = \glob($contentDir . '/*.json') ?: [];

$skipNames = [
    'toc.json',
];

$includeOnlyNames = [
    'item_include.json',
    'tyhpdef_object_include.json',
];

$converted = 0;
$deleted = [];

foreach ($files as $path) {
    $base = \basename($path);

    if (\in_array($base, $skipNames, true)) {
        continue;
    }

    if (\in_array($base, $includeOnlyNames, true)) {
        echoWarn("Leaving include fragment as JSON (deferred includeContent): {$base}");
        continue;
    }

    echoStd("Converting {$base}...");

    $raw = \file_get_contents($path);
    if ($raw === false) {
        echoErr("Failed to read {$path}");
        continue;
    }

    try {
        $json = \json5_decode($raw, true);
    } catch (\Throwable $e) {
        echoErr("Failed to parse {$base}: " . $e->getMessage());
        continue;
    }

    if (!\is_array($json)) {
        echoErr("Invalid JSON in {$base}");
        continue;
    }

    // Non-page files without title/content — skip quietly if not a doc page shape
    if (!isset($json['title']) && !isset($json['content'])) {
        echoWarn("Skipping non-page JSON: {$base}");
        continue;
    }

    $md = convertPage($json, $base);

    if ($base === 'item_template.json') {
        $outPath = $contentDir . '/_template.md';
    } else {
        $outPath = $contentDir . '/' . \preg_replace('/\.json$/i', '.md', $base);
    }

    \file_put_contents($outPath, $md);
    $converted++;

    // Delete converted page JSON (including item_template.json)
    if (!\unlink($path)) {
        echoWarn("Could not delete {$path}");
    } else {
        $deleted[] = $base;
    }
}

// Update toc.json: replace .json" with .md" on item filename lines carefully
$tocPath = $contentDir . '/toc.json';
$tocRaw = \file_get_contents($tocPath);
if ($tocRaw === false) {
    echoErr('Failed to read toc.json');
    exit(1);
}

// Replace item entries: "something.json" → "something.md" but not when it's toc itself
// Prefer string replace on quoted filenames ending in .json that appear in items arrays.
$tocUpdated = \preg_replace_callback(
    '/"([^"]+\.json)"/',
    static function (array $m): string {
        $name = $m[1];
        // Keep non-content references if any; only rewrite content page names
        if ($name === 'toc.json') {
            return $m[0];
        }
        // item_template.json was converted to _template.md
        if ($name === 'item_template.json') {
            return '"_template.md"';
        }

        return '"' . \preg_replace('/\.json$/i', '.md', $name) . '"';
    },
    $tocRaw
);

if ($tocUpdated === null) {
    echoErr('Failed to update toc.json');
    exit(1);
}

\file_put_contents($tocPath, $tocUpdated);

echoStd(GREEN . "Converted {$converted} page(s). Updated toc.json. Deleted " . \count($deleted) . ' JSON file(s).' . NC);
