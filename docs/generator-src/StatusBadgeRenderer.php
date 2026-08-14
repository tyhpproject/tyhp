<?php

declare(strict_types=1);

namespace Tyhp\Docs;

/**
 * Centralized implementation-status badge renderer.
 *
 * Accepts the shared status data shape used by both the item-level `status`
 * field and the inline `status` fence:
 *   { "tier": 0, "story": "08", "state": "planned" }
 *   { "tier": 1, "stories": ["11", "16"], "state": "in-progress" }
 */
final class StatusBadgeRenderer
{
    public function __construct(
        private readonly TemplateEngine $templates,
    ) {
    }

    public function render(?array $status): string
    {
        if (empty($status) || !\is_array($status)) {
            return '';
        }

        $tier = $status['tier'] ?? null;

        $stories = [];
        if (!empty($status['stories']) && \is_array($status['stories'])) {
            $stories = $status['stories'];
        } elseif (!empty($status['story'])) {
            $stories = [$status['story']];
        }

        $stories = \array_values(\array_filter(\array_map(static function ($s) {
            return \trim((string) $s);
        }, $stories), static function ($s) {
            return $s !== '';
        }));

        $labelParts = [];

        if ($tier !== null && $tier !== '') {
            $labelParts[] = 'Tier ' . \htmlentities((string) $tier);
        }

        if (\count($stories) === 1) {
            $labelParts[] = 'Story ' . \htmlentities($stories[0]);
        } elseif (\count($stories) > 1) {
            $escaped = \array_map(static function ($s) {
                return \htmlentities($s);
            }, $stories);
            $labelParts[] = 'Stories ' . \implode(', ', $escaped);
        }

        if (empty($labelParts)) {
            return '';
        }

        $tierStory = \implode(" \u{00b7} ", $labelParts);

        $state = \trim((string) ($status['state'] ?? 'planned'));
        if ($state === '') {
            $state = 'planned';
        }

        $stateStyles = [
            'planned' => 'bg-secondary',
            'in-progress' => 'bg-warning text-dark',
            'in_progress' => 'bg-warning text-dark',
            'inprogress' => 'bg-warning text-dark',
            'done' => 'bg-success',
            'implemented' => 'bg-success',
            'shipped' => 'bg-success',
            'released' => 'bg-success',
            'deprecated' => 'bg-danger',
        ];
        $stateClass = $stateStyles[\strtolower($state)] ?? 'bg-secondary';
        $stateLabel = \htmlentities(\ucwords(\str_replace(['-', '_'], ' ', $state)));
        $stateBadge = \sprintf('<span class="badge %s">%s</span>', $stateClass, $stateLabel);

        $template = $this->templates->load('element-statusBadge_template.html');
        $template = $this->templates->fill($template, '[[TIER_STORY]]', $tierStory);
        $template = $this->templates->fill($template, '[[STATE]]', $stateBadge);

        return $template;
    }
}
