<?php

declare(strict_types=1);

namespace Tyhp\Docs;

use League\CommonMark\Extension\CommonMark\Node\Block\FencedCode;
use League\CommonMark\Node\Node;
use League\CommonMark\Renderer\ChildNodeRendererInterface;
use League\CommonMark\Renderer\NodeRendererInterface;
use Symfony\Component\Yaml\Yaml;

/**
 * Renders fenced code blocks: magic fences (status/classdef/functiondef) or themed code templates.
 */
final class FencedCodeRenderer implements NodeRendererInterface
{
    private const MAGIC_ROLES = ['status', 'classdef', 'functiondef'];

    public function __construct(
        private readonly TemplateEngine $templates,
        private readonly StatusBadgeRenderer $statusBadgeRenderer,
        private readonly SignatureFormatters $signatureFormatters,
    ) {
    }

    public function render(Node $node, ChildNodeRendererInterface $childRenderer): \Stringable|string
    {
        FencedCode::assertInstanceOf($node);

        $info = \trim($node->getInfo() ?? '');
        $literal = $node->getLiteral();
        $tokens = ($info === '') ? [] : (\preg_split('/\s+/', $info) ?: []);

        $magicRole = null;
        foreach ($tokens as $token) {
            $lower = \strtolower($token);
            if (\in_array($lower, self::MAGIC_ROLES, true)) {
                $magicRole = $lower;
                break;
            }
        }

        if ($magicRole !== null) {
            return $this->renderMagic($magicRole, $literal);
        }

        $language = $tokens[0] ?? '';
        $escaped = \htmlspecialchars($literal, \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8');

        return $this->renderNormalCode($language, $escaped);
    }

    private function renderMagic(string $role, string $literal): string
    {
        try {
            $data = Yaml::parse($literal);
        } catch (\Throwable $e) {
            throw new \RuntimeException('Failed to parse YAML for `' . $role . '` fence: ' . $e->getMessage(), 0, $e);
        }

        if (!\is_array($data)) {
            $data = [];
        }

        return match ($role) {
            'status' => $this->statusBadgeRenderer->render($data),
            'classdef' => $this->signatureFormatters->renderClassDef($data),
            'functiondef' => $this->signatureFormatters->renderFunctionDef($data),
            default => throw new \RuntimeException('Unknown magic fence role: ' . $role),
        };
    }

    private function renderNormalCode(string $language, string $escapedContent): string
    {
        $templateFile = match (\strtolower($language)) {
            'tyhp' => 'element-tyhpCode_template.html',
            'php' => 'element-phpCode_template.html',
            'json' => 'element-jsonCode_template.html',
            default => 'element-codeBlock_template.html',
        };

        $template = $this->templates->load($templateFile);

        return $this->templates->fill($template, '[[CONTENT]]', $escapedContent);
    }
}
