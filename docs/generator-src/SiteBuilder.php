<?php

declare(strict_types=1);

namespace Tyhp\Docs;

/**
 * Orchestrates the multi-page static docs build.
 */
final class SiteBuilder
{
    private readonly TemplateEngine $templates;
    private readonly PageParser $pageParser;
    private readonly TocLoader $tocLoader;
    private readonly StatusBadgeRenderer $statusBadgeRenderer;
    private readonly SignatureFormatters $signatureFormatters;
    private readonly MarkdownRenderer $markdownRenderer;

    public function __construct(
        private readonly string $docsRoot,
    ) {
        $templateDir = $this->docsRoot . '/template';
        $this->templates = new TemplateEngine($templateDir);
        $this->pageParser = new PageParser();
        $this->tocLoader = new TocLoader($this->docsRoot . '/content');
        $this->statusBadgeRenderer = new StatusBadgeRenderer($this->templates);
        $this->signatureFormatters = new SignatureFormatters($this->templates);
        $this->markdownRenderer = new MarkdownRenderer(
            $this->templates,
            $this->statusBadgeRenderer,
            $this->signatureFormatters,
        );
    }

    public function build(): void
    {
        \chdir($this->docsRoot);

        $outputDir = $this->docsRoot . '/output';

        Console::echoStd('Clearing previous build...');
        $this->clearDirectory($outputDir);
        if (\is_file($this->docsRoot . '/output.zip')) {
            \unlink($this->docsRoot . '/output.zip');
        }
        if (!\is_dir($outputDir)) {
            \mkdir($outputDir, 0777, true);
        }

        Console::echoStd('Building template and theme assets...');
        $this->copyDirectory($this->docsRoot . '/CoderDocs-BS5-v3.0/assets', $outputDir . '/assets');

        $this->copyFile(
            $this->docsRoot . '/template/tyhp-logo-new.svg',
            $outputDir . '/assets/images/tyhp-logo.svg'
        );
        $this->copyFile(
            $this->docsRoot . '/template/tyhp-logo-new.svg',
            $outputDir . '/assets/images/tyhp-logo-alt.svg'
        );
        $this->copyFile(
            $this->docsRoot . '/template/highlight-tyhp.js',
            $outputDir . '/assets/js/highlight-tyhp.js'
        );

        $returnVar = 0;
        \passthru(
            'sass -q ' . \escapeshellarg($this->docsRoot . '/template/theme.scss')
            . ' ' . \escapeshellarg($outputDir . '/assets/css/theme.css'),
            $returnVar
        );
        if ($returnVar !== 0) {
            throw new \RuntimeException('sass failed with exit code ' . $returnVar);
        }

        Console::echoStd('Loading TOC and pages...');
        $sections = $this->tocLoader->load($this->docsRoot . '/content/toc.json');

        /** @var list<array{sectionIndex: int, itemIndex: int, href: string, title: string, statusHtml: string, bodyHtml: string, sectionSlug: string}> $flatPages */
        $flatPages = [];
        $parsedByHref = [];

        foreach ($sections as $sectionIndex => &$section) {
            foreach ($section['items'] as $itemIndex => &$item) {
                Console::echoStd($item['path']);
                $raw = \file_get_contents($item['path']);
                if ($raw === false) {
                    throw new \RuntimeException('Failed to read ' . $item['path']);
                }

                $parsed = $this->pageParser->parse($raw);
                $title = $parsed['title'] !== '' ? $parsed['title'] : \basename($item['file'], '.md');
                $item['title'] = $title;

                $statusHtml = $this->statusBadgeRenderer->render($parsed['status']);
                $bodyHtml = $this->markdownRenderer->render($parsed['body']);

                $page = [
                    'sectionIndex' => $sectionIndex,
                    'itemIndex' => $itemIndex,
                    'href' => $item['href'],
                    'title' => $title,
                    'statusHtml' => $statusHtml,
                    'bodyHtml' => $bodyHtml,
                    'sectionSlug' => $section['sectionSlug'],
                ];
                $flatPages[] = $page;
                $parsedByHref[$item['href']] = $page;
            }
            unset($item);
        }
        unset($section);

        Console::echoStd('Building index.html...');
        $this->buildIndex($sections, $outputDir);

        Console::echoStd('Building section and item pages...');
        foreach ($sections as $sectionIndex => $section) {
            $this->buildSectionPage($sections, $section, $outputDir);

            foreach ($section['items'] as $itemIndex => $item) {
                $page = $parsedByHref[$item['href']];
                $prev = $flatPages[$this->flatIndex($flatPages, $item['href']) - 1] ?? null;
                $next = $flatPages[$this->flatIndex($flatPages, $item['href']) + 1] ?? null;
                $this->buildItemPage($sections, $section, $page, $prev, $next, $outputDir);
            }
        }

        Console::echoStd('Building output.zip...');
        $returnVar = 0;
        \passthru('zip -q -r output.zip ./output', $returnVar);
        if ($returnVar !== 0) {
            throw new \RuntimeException('zip failed with exit code ' . $returnVar);
        }

        Console::echoStd(Console::GREEN . 'Done.' . Console::NC);
    }

    /**
     * @param list<array{href: string}> $flatPages
     */
    private function flatIndex(array $flatPages, string $href): int
    {
        foreach ($flatPages as $i => $page) {
            if ($page['href'] === $href) {
                return $i;
            }
        }

        return -1;
    }

    /**
     * @param list<array<string, mixed>> $sections
     */
    private function buildIndex(array $sections, string $outputDir): void
    {
        $indexTemplate = $this->templates->load('index_template.html');
        $sectionCardTemplate = $this->templates->load('section-card_template.html');
        $cardList = '';

        foreach ($sections as $section) {
            $sectionCard = $sectionCardTemplate;
            $sectionIcon = \str_replace('"', '\\"', (string) $section['icon']);
            $sectionTitle = $this->wrapInlineCode(\htmlentities((string) $section['section']));
            $sectionText = $this->wrapInlineCode(\htmlentities((string) $section['description']));

            $sectionCard = $this->templates->fill($sectionCard, '[[SECTION_ICON]]', $sectionIcon);
            $sectionCard = $this->templates->fill($sectionCard, '[[SECTION_TITLE]]', $sectionTitle);
            $sectionCard = $this->templates->fill($sectionCard, '[[SECTION_TEXT]]', $sectionText);
            $sectionCard = $this->templates->fill($sectionCard, '[[SECTION_LINK]]', (string) $section['sectionHref']);

            $cardList = \sprintf("%s\n%s", $cardList, $sectionCard);
        }

        $generatedIndex = $this->templates->fill($indexTemplate, '[[REPEAT_SECTION_CARD]]', $cardList);
        \file_put_contents($outputDir . '/index.html', $generatedIndex);
    }

    /**
     * @param list<array<string, mixed>> $sections
     * @param array<string, mixed> $section
     */
    private function buildSectionPage(array $sections, array $section, string $outputDir): void
    {
        $docsTemplate = $this->templates->load('docs-page_template.html');
        $sectionPageTemplate = $this->templates->load('section-page_template.html');

        $nav = $this->buildNav($sections, null, (string) $section['sectionSlug']);

        $childLinks = '';
        foreach ($section['items'] as $item) {
            $title = $this->wrapInlineCode(\htmlentities((string) $item['title']));
            $childLinks .= \sprintf(
                '<li class="mb-2"><a href="%s">%s</a></li>' . "\n",
                \htmlspecialchars((string) $item['href'], \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8'),
                $title
            );
        }

        $pageContent = $sectionPageTemplate;
        $pageContent = $this->templates->fill($pageContent, '[[SECTION_ICON]]', \str_replace('"', '\\"', (string) $section['icon']));
        $pageContent = $this->templates->fill(
            $pageContent,
            '[[SECTION_TITLE]]',
            $this->wrapInlineCode(\htmlentities((string) $section['section']))
        );
        $pageContent = $this->templates->fill(
            $pageContent,
            '[[SECTION_INTRO]]',
            $this->wrapInlineCode(\htmlentities((string) $section['description']))
        );
        $pageContent = $this->templates->fill(
            $pageContent,
            '[[SECTION_LAST_UPDATED]]',
            $this->wrapInlineCode(\htmlentities((string) $section['lastUpdated']))
        );
        $pageContent = $this->templates->fill($pageContent, '[[SECTION_CHILD_LINKS]]', $childLinks);

        $html = $docsTemplate;
        $html = $this->templates->fill($html, '[[PAGE_TITLE]]', \htmlentities((string) $section['section']) . ' - Tyhp');
        $html = $this->templates->fill($html, '[[REPEAT_SECTION_NAV]]', $nav);
        $html = $this->templates->fill($html, '[[PAGE_CONTENT]]', $pageContent);
        $html = $this->templates->fill($html, '[[PREV_NEXT]]', '');

        \file_put_contents($outputDir . '/' . $section['sectionHref'], $html);
    }

    /**
     * @param list<array<string, mixed>> $sections
     * @param array<string, mixed> $section
     * @param array{href: string, title: string, statusHtml: string, bodyHtml: string, sectionSlug: string} $page
     * @param array{href: string, title: string}|null $prev
     * @param array{href: string, title: string}|null $next
     */
    private function buildItemPage(
        array $sections,
        array $section,
        array $page,
        ?array $prev,
        ?array $next,
        string $outputDir,
    ): void {
        $docsTemplate = $this->templates->load('docs-page_template.html');
        $itemContentTemplate = $this->templates->load('item-content_template.html');

        $nav = $this->buildNav($sections, $page['href'], null);

        $itemContent = $itemContentTemplate;
        $itemContent = $this->templates->fill(
            $itemContent,
            '[[ITEM_TITLE]]',
            $this->wrapInlineCode(\htmlentities($page['title']))
        );
        $itemContent = $this->templates->fill($itemContent, '[[ITEM_ID]]', \preg_replace('/[^a-zA-Z0-9_]+/', '_', $page['title']) ?? '');
        $itemContent = $this->templates->fill($itemContent, '[[ITEM_STATUS]]', $page['statusHtml']);
        $itemContent = $this->templates->fill($itemContent, '[[ITEM_CONTENT]]', $page['bodyHtml']);

        $prevNext = $this->buildPrevNext($prev, $next);

        $html = $docsTemplate;
        $html = $this->templates->fill($html, '[[PAGE_TITLE]]', \htmlentities($page['title']) . ' - Tyhp');
        $html = $this->templates->fill($html, '[[REPEAT_SECTION_NAV]]', $nav);
        $html = $this->templates->fill($html, '[[PAGE_CONTENT]]', $itemContent);
        $html = $this->templates->fill($html, '[[PREV_NEXT]]', $prevNext);

        \file_put_contents($outputDir . '/' . $page['href'], $html);
    }

    /**
     * @param list<array<string, mixed>> $sections
     */
    private function buildNav(array $sections, ?string $activeItemHref, ?string $activeSectionSlug): string
    {
        $sectionNavTemplate = $this->templates->load('section-nav_template.html');
        $itemNavTemplate = $this->templates->load('item-nav_template.html');
        $navList = '';

        foreach ($sections as $section) {
            $itemNavList = '';
            foreach ($section['items'] as $item) {
                $itemNav = $itemNavTemplate;
                $isActive = ($activeItemHref !== null && $item['href'] === $activeItemHref);
                $itemNav = $this->templates->fill(
                    $itemNav,
                    '[[ITEM_TITLE]]',
                    $this->wrapInlineCode(\htmlentities((string) $item['title']))
                );
                $itemNav = $this->templates->fill($itemNav, '[[ITEM_HREF]]', (string) $item['href']);
                $itemNav = $this->templates->fill($itemNav, '[[ITEM_ACTIVE_CLASS]]', $isActive ? ' active' : '');
                $itemNavList = \sprintf("%s\n%s", $itemNavList, $itemNav);
            }

            $sectionActive = ($activeSectionSlug !== null && $section['sectionSlug'] === $activeSectionSlug)
                || ($activeItemHref !== null && $this->sectionContainsHref($section, $activeItemHref));

            $sectionNav = $sectionNavTemplate;
            $sectionNav = $this->templates->fill($sectionNav, '[[SECTION_ICON]]', \str_replace('"', '\\"', (string) $section['icon']));
            $sectionNav = $this->templates->fill(
                $sectionNav,
                '[[SECTION_TITLE]]',
                $this->wrapInlineCode(\htmlentities((string) $section['section']))
            );
            $sectionNav = $this->templates->fill($sectionNav, '[[SECTION_HREF]]', (string) $section['sectionHref']);
            $sectionNav = $this->templates->fill($sectionNav, '[[SECTION_ACTIVE_CLASS]]', $sectionActive ? ' active' : '');
            $sectionNav = $this->templates->fill($sectionNav, '[[REPEAT_ITEM_NAV]]', $itemNavList);

            $navList = \sprintf("%s\n%s", $navList, $sectionNav);
        }

        return $navList;
    }

    /**
     * @param array<string, mixed> $section
     */
    private function sectionContainsHref(array $section, string $href): bool
    {
        foreach ($section['items'] as $item) {
            if ($item['href'] === $href) {
                return true;
            }
        }

        return false;
    }

    /**
     * @param array{href: string, title: string}|null $prev
     * @param array{href: string, title: string}|null $next
     */
    private function buildPrevNext(?array $prev, ?array $next): string
    {
        $template = $this->templates->load('prev-next_template.html');

        $prevHtml = '';
        if ($prev !== null) {
            $prevHtml = \sprintf(
                '<a class="btn btn-outline-primary" href="%s">&larr; %s</a>',
                \htmlspecialchars($prev['href'], \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8'),
                $this->wrapInlineCode(\htmlentities($prev['title']))
            );
        }

        $nextHtml = '';
        if ($next !== null) {
            $nextHtml = \sprintf(
                '<a class="btn btn-outline-primary" href="%s">%s &rarr;</a>',
                \htmlspecialchars($next['href'], \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8'),
                $this->wrapInlineCode(\htmlentities($next['title']))
            );
        }

        $template = $this->templates->fill($template, '[[PREV_LINK]]', $prevHtml);
        $template = $this->templates->fill($template, '[[NEXT_LINK]]', $nextHtml);

        return $template;
    }

    private function wrapInlineCode(string $string): string
    {
        return \preg_replace_callback('/`([^`]*)`/', static function ($matches) {
            return '<code>' . $matches[1] . '</code>';
        }, $string) ?? $string;
    }

    private function clearDirectory(string $dir): void
    {
        if (!\is_dir($dir)) {
            return;
        }

        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($dir, \FilesystemIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::CHILD_FIRST
        );

        foreach ($iterator as $fileInfo) {
            /** @var \SplFileInfo $fileInfo */
            if ($fileInfo->isDir()) {
                \rmdir($fileInfo->getPathname());
            } else {
                \unlink($fileInfo->getPathname());
            }
        }
    }

    private function copyDirectory(string $src, string $dst): void
    {
        if (!\is_dir($src)) {
            throw new \RuntimeException('Source directory missing: ' . $src);
        }

        if (!\is_dir($dst)) {
            \mkdir($dst, 0777, true);
        }

        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($src, \FilesystemIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::SELF_FIRST
        );

        foreach ($iterator as $item) {
            /** @var \SplFileInfo $item */
            $target = $dst . \DIRECTORY_SEPARATOR . $iterator->getSubPathName();
            if ($item->isDir()) {
                if (!\is_dir($target)) {
                    \mkdir($target, 0777, true);
                }
            } else {
                $parent = \dirname($target);
                if (!\is_dir($parent)) {
                    \mkdir($parent, 0777, true);
                }
                if (!\copy($item->getPathname(), $target)) {
                    throw new \RuntimeException('Failed to copy ' . $item->getPathname());
                }
            }
        }
    }

    private function copyFile(string $src, string $dst): void
    {
        $parent = \dirname($dst);
        if (!\is_dir($parent)) {
            \mkdir($parent, 0777, true);
        }
        if (!\copy($src, $dst)) {
            throw new \RuntimeException('Failed to copy ' . $src . ' to ' . $dst);
        }
    }
}
