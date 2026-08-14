<?php

declare(strict_types=1);

namespace Tyhp\Docs;

/**
 * Formats classDef / functionDef YAML into highlighted Tyhp code HTML.
 */
final class SignatureFormatters
{
    public function __construct(
        private readonly TemplateEngine $templates,
    ) {
    }

    public function renderClassDef(array $content): string
    {
        $elementContentObjectDecl = '';

        if (!empty($content['modifiers'])) {
            $elementContentObjectDecl = (string) $content['modifiers'];
        }

        if (!empty($elementContentObjectDecl)) {
            $elementContentObjectDecl .= ' ';
        }

        $elementContentObjectDecl .= ($content['type'] ?? '') . ' ' . ($content['identifier'] ?? '');

        $elementContentObjectDecl .= $this->formatGenericParamters($content);

        if (!empty($content['extends'])) {
            $elementContentExtends = (string) $content['extends'];
            if ($elementContentExtends !== '') {
                if (($content['type'] ?? '') === 'enum') {
                    $elementContentObjectDecl .= ': ' . $elementContentExtends;
                } else {
                    $elementContentObjectDecl .= ' extends ' . $elementContentExtends;
                }
            }
        }

        $elementContentImplementsList = $content['implements']
            ?? $content['implments']
            ?? [];
        $elementContentImplements = '';
        if (!empty($elementContentImplementsList) && \is_array($elementContentImplementsList)) {
            foreach ($elementContentImplementsList as $implementsInterface) {
                if ($elementContentImplements === '') {
                    $elementContentImplements = (string) $implementsInterface;
                } else {
                    $elementContentImplements = \sprintf('%s, %s', $elementContentImplements, $implementsInterface);
                }
            }
        }
        $elementContentImplements = \trim($elementContentImplements);

        if ($elementContentImplements !== '') {
            $elementContentObjectDecl .= ' implements ' . $elementContentImplements;
        }

        $elementContentObjectMembers = '';
        foreach ($content['members'] ?? [] as $itemSubcontent) {
            if (!\is_array($itemSubcontent)) {
                continue;
            }

            $memberContentType = $itemSubcontent['type'] ?? '';
            $memberContent = '';

            switch ($memberContentType) {
                case 'comment':
                    $memberContent = ' /* ' . ($itemSubcontent['content'] ?? '') . ' */';
                    break;
                case 'newLine':
                    $memberContent = "\n";
                    break;
                case 'const':
                    $memberContent = $this->formatMemberConstDef($itemSubcontent) . ';';
                    break;
                case 'property':
                    $memberContent = $this->formatMemberPropertyDef($itemSubcontent) . ';';
                    break;
                case 'propertyAccessor':
                    $memberContent = $this->formatMemberPropertyAccessorDef($itemSubcontent);
                    break;
                case 'typeAlias':
                    $memberContent = $this->formatMemberTypeAliasDef($itemSubcontent) . ';';
                    break;
                case 'method':
                    $memberContent = $this->formatFunctionDef($itemSubcontent['def'] ?? []);
                    break;
                case 'operator':
                    $memberContent = $this->formatFunctionDef($itemSubcontent['def'] ?? [], true);
                    break;
                case 'enumCase':
                    $memberContent = $this->formatMemberEnumCaseDef($itemSubcontent) . ';';
                    break;
                default:
                    break;
            }

            $memberContent = \trim($memberContent);

            if ($elementContentObjectMembers === '') {
                $elementContentObjectMembers = '    ' . $memberContent;
            } else {
                $elementContentObjectMembers = \sprintf("%s\n    %s", $elementContentObjectMembers, $memberContent);
            }
        }

        $elementContent = \sprintf("%s\n{\n%s\n}", $elementContentObjectDecl, $elementContentObjectMembers);
        $elementContent = \trim($elementContent);
        $elementContent = \htmlspecialchars($elementContent, \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8');

        $template = $this->templates->load('element-tyhpCode_template.html');

        return $this->templates->fill($template, '[[CONTENT]]', $elementContent);
    }

    public function renderFunctionDef(array $content): string
    {
        $elementContent = $this->formatFunctionDef($content);
        $elementContent = \trim($elementContent);
        $elementContent = \htmlspecialchars($elementContent, \ENT_QUOTES | \ENT_SUBSTITUTE, 'UTF-8');

        $template = $this->templates->load('element-tyhpCode_template.html');

        return $this->templates->fill($template, '[[CONTENT]]', $elementContent);
    }

    public function formatMemberConstDef(array $block): string
    {
        if (empty($block['def']['identifier'])) {
            Console::echoErr("Missing 'identifier' element for const.", true);
        }

        if (empty($block['def']['type'])) {
            Console::echoErr("Missing 'type' element for const", true);
        }

        if (empty($block['def']['value'])) {
            Console::echoErr("Missing 'value' element for const.", true);
        }

        $elementContentModifiers = '';

        if (!empty($block['def']['modifiers'])) {
            $elementContentModifiers = (string) $block['def']['modifiers'];
            $elementContentModifiers = \trim($elementContentModifiers);

            if ($elementContentModifiers !== '') {
                $elementContentModifiers .= ' ';
            }
        }

        $elementContentIdentifier = \trim((string) $block['def']['identifier']);
        $elementContentType = \trim((string) $block['def']['type']);
        $elementContentValue = \trim((string) $block['def']['value']);

        return \sprintf('%sconst %s %s = %s', $elementContentModifiers, $elementContentType, $elementContentIdentifier, $elementContentValue);
    }

    public function formatMemberPropertyDef(array $block): string
    {
        if (empty($block['def']['identifier'])) {
            Console::echoErr("Missing 'identifier' element for property.", true);
        }

        if (empty($block['def']['type'])) {
            Console::echoErr("Missing 'type' element for property.", true);
        }

        $elementContentModifiers = '';
        if (!empty($block['def']['modifiers'])) {
            $elementContentModifiers = (string) $block['def']['modifiers'];
            $elementContentModifiers = \trim($elementContentModifiers);
            if ($elementContentModifiers !== '') {
                $elementContentModifiers .= ' ';
            }
        }

        $elementContentIdentifier = \trim((string) $block['def']['identifier']);
        $elementContentType = \trim((string) $block['def']['type']);

        $elementContentValue = '';
        if (!empty($block['def']['value'])) {
            $elementContentValue = (string) $block['def']['value'];
            if ($elementContentValue !== '') {
                $elementContentValue = ' = ' . \trim($elementContentValue);
            }
        }

        return \sprintf('%s%s %s%s', $elementContentModifiers, $elementContentType, $elementContentIdentifier, $elementContentValue);
    }

    public function formatMemberPropertyAccessorDef(array $block): string
    {
        if (empty($block['def']['identifier'])) {
            Console::echoErr("Missing 'identifier' element for propertyAccessor.", true);
        }

        if (empty($block['def']['type'])) {
            Console::echoErr("Missing 'type' element for propertyAccessor.", true);
        }

        if (empty($block['def']['accessors'])) {
            Console::echoErr("Missing 'accessors' element for propertyAccessor.", true);
        }

        $elementContentModifiers = '';
        if (!empty($block['def']['modifiers'])) {
            $elementContentModifiers = (string) $block['def']['modifiers'];
            $elementContentModifiers = \trim($elementContentModifiers);
            if ($elementContentModifiers !== '') {
                $elementContentModifiers .= ' ';
            }
        }

        $elementContentIdentifier = \trim((string) $block['def']['identifier']);
        $elementContentType = \trim((string) $block['def']['type']);

        $elementContentAccessors = '';
        foreach ($block['def']['accessors'] ?? [] as $itemSubcontent) {
            $elementContentAccessors .= ' ' . $itemSubcontent . ';';
        }

        $elementContentAccessors = \trim($elementContentAccessors);

        return \sprintf('%s%s %s { %s }', $elementContentModifiers, $elementContentType, $elementContentIdentifier, $elementContentAccessors);
    }

    public function formatMemberTypeAliasDef(array $block, bool $operatorDef = false): string
    {
        if (empty($block['def']['identifier'])) {
            Console::echoErr("Missing 'identifier' element for typeAlias.", true);
        }

        if (empty($block['def']['value'])) {
            Console::echoErr("Missing 'value' element for typeAlias.", true);
        }

        $elementContentModifiers = '';
        if (!empty($block['def']['modifiers'])) {
            $elementContentModifiers = (string) $block['def']['modifiers'];
            $elementContentModifiers = \trim($elementContentModifiers);
            if ($elementContentModifiers !== '') {
                $elementContentModifiers .= ' ';
            }
        }

        $elementContentIdentifier = \trim((string) $block['def']['identifier']);

        if (!$operatorDef) {
            $elementContentGenerics = $this->formatGenericParamters($block['def']);
        } else {
            $elementContentGenerics = '';
        }

        $elementContentValue = \trim((string) $block['def']['value']);

        return \sprintf('%stype %s%s = %s', $elementContentModifiers, $elementContentIdentifier, $elementContentGenerics, $elementContentValue);
    }

    public function formatMemberEnumCaseDef(array $block): string
    {
        if (empty($block['def']['identifier'])) {
            Console::echoErr("Missing 'identifier' element for enumCase.", true);
        }

        $elementContentIdentifier = \trim((string) $block['def']['identifier']);

        $elementContentValue = '';
        if (!empty($block['def']['value'])) {
            $elementContentValue = (string) $block['def']['value'];
            if ($elementContentValue !== '') {
                $elementContentValue = \trim($elementContentValue);
            }
        }

        if ($elementContentValue !== '') {
            $elementContentValue = ' = ' . $elementContentValue;
        }

        return \sprintf('case %s%s', $elementContentIdentifier, $elementContentValue);
    }

    public function formatFunctionDef(array $block, bool $operatorDef = false): string
    {
        $elementContentModifiers = '';
        if (!$operatorDef) {
            if (!empty($block['modifiers'])) {
                $elementContentModifiers = \trim((string) ($block['modifiers'] ?? ''));
                if ($elementContentModifiers !== '') {
                    $elementContentModifiers .= ' ';
                }
            }
        }

        $elementContentIdentifier = \trim((string) ($block['identifier'] ?? ''));
        $elementContentReturnType = \trim((string) ($block['returnType'] ?? ''));

        if (!$operatorDef) {
            $elementContentGenerics = $this->formatGenericParamters($block);
        } else {
            $elementContentGenerics = '';
        }

        $elementContentParameters = '';
        foreach ($block['parameters'] ?? [] as $itemSubcontent) {
            if (!\is_array($itemSubcontent)) {
                continue;
            }

            $parameterContent = ($itemSubcontent['type'] ?? '') . ' ' . ($itemSubcontent['identifier'] ?? '');

            if (!empty($itemSubcontent['value'])) {
                $parameterDefault = (string) ($itemSubcontent['value'] ?? '');
                if ($parameterDefault !== '') {
                    $parameterContent .= ' = ' . $parameterDefault;
                }
            }

            if ($elementContentParameters === '') {
                $elementContentParameters = $parameterContent;
            } else {
                $elementContentParameters = \sprintf('%s, %s', $elementContentParameters, $parameterContent);
            }
        }
        $elementContentParameters = \trim($elementContentParameters);

        $elementKeyword = 'function';
        if ($operatorDef) {
            $elementKeyword = 'operator';
        }

        return \sprintf('%s%s %s%s(%s): %s', $elementContentModifiers, $elementKeyword, $elementContentIdentifier, $elementContentGenerics, $elementContentParameters, $elementContentReturnType);
    }

    public function formatGenericParamters(array $block): string
    {
        $elementContentGenerics = '';
        if (!empty($block['generics'])) {
            foreach ($block['generics'] ?? [] as $itemSubcontent) {
                if (!\is_array($itemSubcontent)) {
                    continue;
                }

                $genericParameter = (string) ($itemSubcontent['identifier'] ?? '');
                if (!empty($itemSubcontent['extends'])) {
                    $genericExtends = (string) ($itemSubcontent['extends'] ?? '');
                    if ($genericExtends !== '') {
                        $genericParameter .= ' extends ' . $genericExtends;
                    }
                }
                if ($elementContentGenerics === '') {
                    $elementContentGenerics = $genericParameter;
                } else {
                    $elementContentGenerics = \sprintf('%s, %s', $elementContentGenerics, $genericParameter);
                }
            }
        }
        $elementContentGenerics = \trim($elementContentGenerics);

        if ($elementContentGenerics !== '') {
            return \sprintf('<%s>', $elementContentGenerics);
        }

        return '';
    }
}
