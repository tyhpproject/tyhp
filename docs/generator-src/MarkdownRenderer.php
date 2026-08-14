<?php

declare(strict_types=1);

namespace Tyhp\Docs;

use League\CommonMark\Environment\Environment;
use League\CommonMark\Extension\CommonMark\CommonMarkCoreExtension;
use League\CommonMark\Extension\CommonMark\Node\Block\FencedCode;
use League\CommonMark\MarkdownConverter;
use League\CommonMark\Util\HtmlFilter;

/**
 * Renders Markdown body to HTML with admonitions and magic/themed fences.
 */
final class MarkdownRenderer
{
    private readonly MarkdownConverter $converter;

    private const CALLOUT_TYPES = ['tip', 'note', 'warning', 'danger'];

    public function __construct(
        private readonly TemplateEngine $templates,
        private readonly StatusBadgeRenderer $statusBadgeRenderer,
        private readonly SignatureFormatters $signatureFormatters,
    ) {
        $config = [
            'html_input' => HtmlFilter::ALLOW,
            'allow_unsafe_links' => false,
            'renderer' => [
                'soft_break' => "<br />\n",
            ],
        ];

        $environment = new Environment($config);
        $environment->addExtension(new CommonMarkCoreExtension());
        $environment->addRenderer(
            FencedCode::class,
            new FencedCodeRenderer($this->templates, $this->statusBadgeRenderer, $this->signatureFormatters)
        );

        $this->converter = new MarkdownConverter($environment);
    }

    public function render(string $markdown): string
    {
        // Extract admonitions to placeholders so CommonMark never sees HTML that
        // contains blank lines (which would end an HTML block early).
        [$withPlaceholders, $replacements] = $this->extractAdmonitions($markdown);
        $html = (string) $this->converter->convert($withPlaceholders);

        foreach ($replacements as $token => $fragment) {
            $html = \str_replace($token, $fragment, $html);
        }

        return $html;
    }

    /**
     * Extract :::type … ::: blocks; closer is a line that is exactly `:::`.
     * Inner Markdown is rendered via CommonMark (one level; no nested admonitions).
     *
     * @return array{0: string, 1: array<string, string>}
     */
    private function extractAdmonitions(string $markdown): array
    {
        $lines = \preg_split('/\r\n|\r|\n/', $markdown);
        if ($lines === false) {
            return [$markdown, []];
        }

        $out = [];
        $replacements = [];
        $i = 0;
        $n = \count($lines);
        $counter = 0;

        while ($i < $n) {
            $line = $lines[$i];
            if (\preg_match('/^:::(tip|note|warning|danger|alert|member)(.*)$/', $line, $m) !== 1) {
                $out[] = $line;
                $i++;
                continue;
            }

            $type = $m[1];
            $rest = $m[2];
            $i++;

            $bodyLines = [];
            $foundClose = false;
            while ($i < $n) {
                if ($lines[$i] === ':::') {
                    $foundClose = true;
                    $i++;
                    break;
                }
                $bodyLines[] = $lines[$i];
                $i++;
            }

            if (!$foundClose) {
                throw new \RuntimeException('Unclosed admonition starting with :::' . $type);
            }

            $innerMarkdown = \implode("\n", $bodyLines);
            $innerHtml = (string) $this->converter->convert($innerMarkdown);
            $fragment = $this->wrapAdmonition($type, $rest, $innerHtml);

            $token = '<!--ADMONITIONPLACEHOLDER' . $counter . 'END-->';
            $counter++;
            $replacements[$token] = $fragment;
            $out[] = $token;
        }

        return [\implode("\n", $out), $replacements];
    }

    private function wrapAdmonition(string $type, string $rest, string $innerHtml): string
    {
        $rest = \trim($rest);

        if ($type === 'member') {
            $identifier = '';
            $title = '';
            // Identifiers may contain ']' (e.g. `new X(...) with [...]`); take up to the last ']'.
            if (\preg_match('/^\[(.*)\](?:\s+(.*))?$/s', $rest, $m) === 1) {
                $identifier = $m[1];
                $title = isset($m[2]) ? \trim($m[2]) : '';
            } else {
                throw new \RuntimeException('Invalid :::member syntax; expected :::member[identifier]');
            }

            // Optional title after identifier is ignored for the member template
            // (identifier is the heading); keep content as rendered HTML.
            unset($title);

            $template = $this->templates->load('element-memberDescription_template.html');
            $template = $this->templates->fill($template, '[[IDENTIFIER]]', \htmlspecialchars($identifier, \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8'));
            $template = $this->templates->fill($template, '[[CONTENT]]', $innerHtml);

            return $template;
        }

        if ($type === 'alert') {
            $style = 'primary';
            if (\preg_match('/^\{style=([^}]+)\}(?:\s+(.*))?$/', $rest, $m) === 1) {
                $style = \trim($m[1]);
                $rest = isset($m[2]) ? \trim($m[2]) : '';
            }
            $style = \str_replace('"', '', $style);

            $template = $this->templates->load('element-alert_template.html');
            $template = $this->templates->fill($template, '[[STYLE]]', $style);
            $template = $this->templates->fill($template, '[[CONTENT]]', $innerHtml);

            return $template;
        }

        if (\in_array($type, self::CALLOUT_TYPES, true)) {
            $defaultTitles = [
                'tip' => 'Tip',
                'note' => 'Note',
                'warning' => 'Warning',
                'danger' => 'Danger',
            ];
            $title = $rest !== '' ? $rest : $defaultTitles[$type];
            $title = \htmlspecialchars($title, \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8');

            $templateFile = match ($type) {
                'tip' => 'element-tip_template.html',
                'note' => 'element-note_template.html',
                'warning' => 'element-warning_template.html',
                'danger' => 'element-danger_template.html',
            };

            $template = $this->templates->load($templateFile);
            $template = $this->templates->fill($template, '[[TITLE]]', $title);
            $template = $this->templates->fill($template, '[[CONTENT]]', $innerHtml);

            return $template;
        }

        throw new \RuntimeException('Unknown admonition type: ' . $type);
    }
}
