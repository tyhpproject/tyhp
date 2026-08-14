<?php

declare(strict_types=1);

namespace Tyhp\Docs;

use Symfony\Component\Yaml\Yaml;

/**
 * Parses Markdown page files with YAML front matter.
 *
 * @phpstan-type PageData array{title: string, status: ?array, body: string}
 */
final class PageParser
{
    /**
     * @return array{title: string, status: ?array, body: string}
     */
    public function parse(string $markdownFileContents): array
    {
        $title = '';
        $status = null;
        $body = $markdownFileContents;

        if (\preg_match('/\A---\r?\n(.*?)\r?\n---\r?\n(.*)\z/s', $markdownFileContents, $matches) === 1) {
            $frontMatter = Yaml::parse($matches[1]);
            if (!\is_array($frontMatter)) {
                $frontMatter = [];
            }

            $title = isset($frontMatter['title']) ? (string) $frontMatter['title'] : '';
            if (\array_key_exists('status', $frontMatter) && \is_array($frontMatter['status'])) {
                $status = $frontMatter['status'];
            }
            $body = $matches[2];
        }

        return [
            'title' => $title,
            'status' => $status,
            'body' => $body,
        ];
    }
}
