<?php

declare(strict_types=1);

namespace Tyhp\Docs;

/**
 * Loads docs/content/toc.json (JSON5) and resolves Markdown page items.
 */
final class TocLoader
{
    public function __construct(
        private readonly string $contentDir,
    ) {
    }

    /**
     * @return list<array{
     *   icon: string,
     *   description: string,
     *   lastUpdated: string,
     *   section: string,
     *   sectionSlug: string,
     *   sectionHref: string,
     *   items: list<array{file: string, path: string, href: string, title: string}>
     * }>
     */
    public function load(string $tocPath): array
    {
        $raw = \file_get_contents($tocPath);
        if ($raw === false) {
            throw new \RuntimeException('Failed to read TOC: ' . $tocPath);
        }

        $tocJson = \json5_decode($raw, true);
        if (!\is_array($tocJson)) {
            throw new \RuntimeException('Invalid TOC JSON5: ' . $tocPath);
        }

        $sections = [];

        foreach ($tocJson['content'] ?? [] as $sectionData) {
            if (!\is_array($sectionData)) {
                continue;
            }

            $sectionTitle = (string) ($sectionData['section'] ?? '');
            $sectionSlug = 'section_' . \preg_replace('/[^a-zA-Z0-9_]+/', '_', $sectionTitle);
            $sectionHref = $sectionSlug . '.html';
            $manualLastUpdated = \trim((string) ($sectionData['lastUpdated'] ?? ''));
            $calcLastUpdated = ($manualLastUpdated === '');

            $items = [];
            $maxMtime = 0;

            foreach ($sectionData['items'] ?? [] as $itemFileName) {
                $itemFileName = (string) $itemFileName;
                if (!\str_ends_with(\strtolower($itemFileName), '.md')) {
                    throw new \RuntimeException(
                        'TOC item must be a .md file (got "' . $itemFileName . '" in section "' . $sectionTitle . '")'
                    );
                }

                $path = $this->contentDir . '/' . $itemFileName;
                if (!\is_file($path)) {
                    throw new \RuntimeException(
                        'Missing content file for section "' . $sectionTitle . '": ' . $itemFileName
                    );
                }

                $mtime = \filemtime($path);
                if ($mtime !== false && $mtime > $maxMtime) {
                    $maxMtime = $mtime;
                }

                $basename = \basename($itemFileName, '.md');
                $items[] = [
                    'file' => $itemFileName,
                    'path' => $path,
                    'href' => $basename . '.html',
                    'title' => '',
                ];
            }

            if ($calcLastUpdated) {
                $lastUpdated = $maxMtime > 0
                    ? \date('Y-m-d H:i:sO', $maxMtime)
                    : '';
            } else {
                $lastUpdated = $manualLastUpdated;
            }

            $sections[] = [
                'icon' => (string) ($sectionData['icon'] ?? ''),
                'description' => (string) ($sectionData['description'] ?? ''),
                'lastUpdated' => $lastUpdated,
                'section' => $sectionTitle,
                'sectionSlug' => $sectionSlug,
                'sectionHref' => $sectionHref,
                'items' => $items,
            ];
        }

        return $sections;
    }
}
