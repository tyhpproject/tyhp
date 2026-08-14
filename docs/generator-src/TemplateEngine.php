<?php

declare(strict_types=1);

namespace Tyhp\Docs;

/**
 * Literal [[PLACEHOLDER]] replacement for HTML templates under docs/template/.
 */
final class TemplateEngine
{
    public function __construct(
        private readonly string $templateDir,
    ) {
    }

    /**
     * Replace a literal [[PLACEHOLDER]] token in a template with a value.
     *
     * Uses \str_replace (literal replacement) instead of \preg_replace so that
     * user content containing "$" or "\" sequences renders verbatim.
     */
    public function fill(string $template, string $placeholder, string $value): string
    {
        return \str_replace($placeholder, $value, $template);
    }

    public function load(string $relativePath): string
    {
        $path = $this->templateDir . '/' . \ltrim($relativePath, '/');
        $contents = \file_get_contents($path);
        if ($contents === false) {
            throw new \RuntimeException('Failed to load template: ' . $path);
        }

        return $contents;
    }
}
