<?php

declare(strict_types=1);

$finder = PhpCsFixer\Finder::create()
    ->in([
        __DIR__ . '/packages/core/src',
        __DIR__ . '/packages/core/tests',
        __DIR__ . '/packages/decimal/src',
        __DIR__ . '/packages/decimal/tests',
        __DIR__ . '/packages/async/src',
        __DIR__ . '/packages/async/tests',
    ])
    ->name('*.php');

return (new PhpCsFixer\Config())
    ->setRiskyAllowed(true)
    ->setRules([
        '@PSR12' => true,
        'strict_param' => true,
        'declare_strict_types' => true,
        'ordered_imports' => ['sort_algorithm' => 'alpha'],
        'no_unused_imports' => true,
        'trailing_comma_in_multiline' => ['elements' => ['arrays', 'arguments', 'parameters']],
        'single_quote' => true,
        'no_empty_comment' => true,
        'no_empty_statement' => true,
        'no_extra_blank_lines' => true,
        'array_syntax' => ['syntax' => 'short'],
    ])
    ->setFinder($finder);
