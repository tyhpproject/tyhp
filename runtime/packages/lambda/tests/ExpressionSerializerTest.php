<?php

declare(strict_types=1);

namespace Tyhp\Tests\Lambda;

use PHPUnit\Framework\TestCase;
use Tyhp\Expression;
use Tyhp\Expression\ConstantExpression;
use Tyhp\Expression\ExpressionSerializer;
use Tyhp\Expression\ParameterExpression;
use Tyhp\Expression\PropertyAccessExpression;

class ExpressionSerializerTest extends TestCase
{
    public function testToJsonIncludesParametersAndBody(): void
    {
        $param = new ParameterExpression('source', 'object', 0);
        $body = new PropertyAccessExpression($param, 'name', 'string');
        $expression = new Expression(
            $body,
            [$param],
            fn(object $source): string => $source->name,
            'string',
        );

        $json = ExpressionSerializer::toJson($expression);

        self::assertStringContainsString('"returnType": "string"', $json);
        self::assertStringContainsString('"nodeType": "propertyAccess"', $json);
        self::assertStringContainsString('"name": "source"', $json);
    }

    public function testEqualsComparesStructuralJson(): void
    {
        $makeExpression = static function (): Expression {
            $param = new ParameterExpression('value', 'int', 0);
            $body = new ConstantExpression(0, 'int');
            return new Expression(
                $body,
                [$param],
                fn(int $value): int => $value,
                'int',
            );
        };

        $left = $makeExpression();
        $right = $makeExpression();

        self::assertTrue(ExpressionSerializer::equals($left, $right));
    }

    public function testEqualsDetectsDifferentBodies(): void
    {
        $param = new ParameterExpression('value', 'int', 0);
        $left = new Expression(
            new ConstantExpression(1, 'int'),
            [$param],
            fn(int $value): int => $value,
            'int',
        );
        $right = new Expression(
            new ConstantExpression(2, 'int'),
            [$param],
            fn(int $value): int => $value,
            'int',
        );

        self::assertFalse(ExpressionSerializer::equals($left, $right));
    }

    public function testInstanceEqualsDelegatesToSerializer(): void
    {
        $param = new ParameterExpression('value', 'int', 0);
        $left = new Expression(
            new ConstantExpression(1, 'int'),
            [$param],
            fn(int $value): int => $value,
            'int',
        );
        $right = new Expression(
            new ConstantExpression(1, 'int'),
            [$param],
            fn(int $value): int => $value,
            'int',
        );

        self::assertTrue($left->equals($right));
    }

    public function testToJsonSerializesInstanceofNode(): void
    {
        $param = new ParameterExpression('value', 'mixed', 0);
        $body = new \Tyhp\Expression\InstanceofExpression(
            $param,
            'int',
            'bool',
        );
        $expression = new Expression(
            $body,
            [$param],
            fn(mixed $value): bool => $value instanceof \stdClass,
            'bool',
        );

        $json = ExpressionSerializer::toJson($expression);

        self::assertStringContainsString('"nodeType": "instanceof"', $json);
        self::assertStringContainsString('"targetType": "int"', $json);
    }
}
