<?php

declare(strict_types=1);

namespace Tyhp\Tests\Decimal;

use PHPUnit\Framework\TestCase;
use Tyhp\Backend\DecimalBackend;
use Tyhp\Contracts\DecimalConvertible;
use Tyhp\Decimal;

final class DecimalTest extends TestCase
{
    // -------------------------------------------------------
    // Construction
    // -------------------------------------------------------

    public function testConstructFromInt(): void
    {
        $d = new Decimal(42);
        $this->assertSame('42', $d->value);
        $this->assertSame(0, $d->scale);
    }

    public function testConstructFromFloat(): void
    {
        $d = new Decimal(3.14);
        $this->assertSame(3.14, $d->__toFloat());
        $this->assertGreaterThan(0, $d->scale);
    }

    public function testConstructFromString(): void
    {
        $d = new Decimal('123.456');
        $this->assertSame('123.456', $d->value);
        $this->assertSame(3, $d->scale);
    }

    public function testConstructFromNull(): void
    {
        $d = new Decimal(null);
        $this->assertSame('0', $d->value);
        $this->assertSame(0, $d->scale);
    }

    public function testConstructFromDecimalConvertible(): void
    {
        $convertable = new class implements DecimalConvertible {
            public function __toDecimal(): Decimal
            {
                return new Decimal('99.99');
            }
        };

        $d = new Decimal($convertable);
        $this->assertTrue($d->equals('99.99'));
    }

    public function testConstructInvalidStringThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        new Decimal('not-a-number');
    }

    public function testConstructNanThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        new Decimal(\NAN);
    }

    public function testConstructInfThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        new Decimal(\INF);
    }

    public function testConstructWithScale(): void
    {
        $d = new Decimal('1.5', 4);
        $this->assertSame(4, $d->scale);
        $this->assertSame('1.5000', $d->value);
    }

    // -------------------------------------------------------
    // Arithmetic
    // -------------------------------------------------------

    public function testAdd(): void
    {
        $result = (new Decimal('10.5'))->add('3.2');
        $this->assertTrue($result->equals('13.7'));
    }

    public function testSubtract(): void
    {
        $result = (new Decimal('10.5'))->subtract('3.2');
        $this->assertTrue($result->equals('7.3'));
    }

    public function testMultiply(): void
    {
        $result = (new Decimal('4.0'))->multiply('2.5');
        $this->assertTrue($result->equals('10.0'));
    }

    public function testDivide(): void
    {
        $result = (new Decimal('10.0'))->divide('4.0');
        $this->assertTrue($result->equals('2.5'));
    }

    public function testDivideByZeroThrows(): void
    {
        $this->expectException(\DivisionByZeroError::class);
        (new Decimal('10'))->divide('0');
    }

    public function testModulo(): void
    {
        $result = (new Decimal('10'))->modulo('3');
        $this->assertTrue($result->equals('1'));
    }

    public function testPower(): void
    {
        $result = (new Decimal('3'))->power(3);
        $this->assertTrue($result->equals('27'));
    }

    public function testPowerNegativeExponentThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        (new Decimal('2'))->power(-1);
    }

    public function testNegate(): void
    {
        $pos = new Decimal('5.5');
        $neg = $pos->negate();
        $this->assertTrue($neg->equals('-5.5'));

        $backToPos = $neg->negate();
        $this->assertTrue($backToPos->equals('5.5'));
    }

    public function testAbs(): void
    {
        $neg = new Decimal('-7.3');
        $this->assertTrue($neg->abs()->equals('7.3'));

        $pos = new Decimal('7.3');
        $this->assertTrue($pos->abs()->equals('7.3'));
    }

    public function testSqrt(): void
    {
        $result = (new Decimal('9'))->sqrt(2);
        $this->assertTrue($result->equals('3.00'));
    }

    public function testSqrtNegativeThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        (new Decimal('-4'))->sqrt();
    }

    // -------------------------------------------------------
    // Comparison
    // -------------------------------------------------------

    public function testCompareTo(): void
    {
        $a = new Decimal('10');
        $this->assertSame(-1, $a->compareTo('20'));
        $this->assertSame(0, $a->compareTo('10'));
        $this->assertSame(1, $a->compareTo('5'));
    }

    public function testEquals(): void
    {
        $a = new Decimal('5.00');
        $this->assertTrue($a->equals('5'));
        $this->assertFalse($a->equals('6'));
    }

    public function testGreaterThan(): void
    {
        $a = new Decimal('10');
        $this->assertTrue($a->greaterThan('5'));
        $this->assertFalse($a->greaterThan('10'));
        $this->assertFalse($a->greaterThan('15'));
    }

    public function testLessThan(): void
    {
        $a = new Decimal('5');
        $this->assertTrue($a->lessThan('10'));
        $this->assertFalse($a->lessThan('5'));
        $this->assertFalse($a->lessThan('2'));
    }

    public function testGreaterThanOrEqual(): void
    {
        $a = new Decimal('10');
        $this->assertTrue($a->greaterThanOrEqual('10'));
        $this->assertTrue($a->greaterThanOrEqual('5'));
        $this->assertFalse($a->greaterThanOrEqual('15'));
    }

    public function testLessThanOrEqual(): void
    {
        $a = new Decimal('10');
        $this->assertTrue($a->lessThanOrEqual('10'));
        $this->assertTrue($a->lessThanOrEqual('15'));
        $this->assertFalse($a->lessThanOrEqual('5'));
    }

    public function testIsZero(): void
    {
        $this->assertTrue((new Decimal('0'))->isZero());
        $this->assertTrue((new Decimal('0.00'))->isZero());
        $this->assertFalse((new Decimal('1'))->isZero());
    }

    public function testIsPositive(): void
    {
        $this->assertTrue((new Decimal('5'))->isPositive());
        $this->assertFalse((new Decimal('0'))->isPositive());
        $this->assertFalse((new Decimal('-3'))->isPositive());
    }

    public function testIsNegative(): void
    {
        $this->assertTrue((new Decimal('-3'))->isNegative());
        $this->assertFalse((new Decimal('0'))->isNegative());
        $this->assertFalse((new Decimal('5'))->isNegative());
    }

    // -------------------------------------------------------
    // Rounding
    // -------------------------------------------------------

    public function testRound(): void
    {
        if (!\extension_loaded('bcmath')) {
            $this->markTestSkipped('Requires bcmath extension for precision tests');
        }

        $d = new Decimal('3.456');
        $rounded = $d->round(2);
        $this->assertSame('3.46', $rounded->value);
        $this->assertSame(2, $rounded->scale);
    }

    public function testRoundHalfUp(): void
    {
        if (!\extension_loaded('bcmath')) {
            $this->markTestSkipped('Requires bcmath extension for precision tests');
        }

        $d = new Decimal('2.555');
        $rounded = $d->round(2, \PHP_ROUND_HALF_UP);
        $this->assertSame('2.56', $rounded->value);
    }

    public function testFloor(): void
    {
        $this->assertSame('3', (new Decimal('3.7'))->floor()->value);
        $this->assertSame('-4', (new Decimal('-3.2'))->floor()->value);
    }

    public function testCeil(): void
    {
        $this->assertSame('4', (new Decimal('3.2'))->ceil()->value);
        $this->assertSame('-3', (new Decimal('-3.7'))->ceil()->value);
    }

    public function testWithScale(): void
    {
        if (!\extension_loaded('bcmath')) {
            $this->markTestSkipped('Requires bcmath extension for precision tests');
        }

        $d = new Decimal('5.1');
        $scaled = $d->withScale(4);
        $this->assertSame(4, $scaled->scale);
        $this->assertSame('5.1000', $scaled->value);
    }

    // -------------------------------------------------------
    // Conversion
    // -------------------------------------------------------

    public function testToInt(): void
    {
        $d = new Decimal('42.9');
        $this->assertSame(42, $d->__toInt());
    }

    public function testToFloat(): void
    {
        $d = new Decimal('3.14');
        $this->assertSame(3.14, $d->__toFloat());
    }

    public function testAsDecimal(): void
    {
        $d = new Decimal('100');
        $this->assertSame($d, $d->__toDecimal());
    }

    // -------------------------------------------------------
    // Formatting
    // -------------------------------------------------------

    public function testToString(): void
    {
        $d = new Decimal('123.45');
        $this->assertSame('123.45', (string) $d);
    }

    public function testJsonSerialize(): void
    {
        $d = new Decimal('99.99');
        $this->assertSame('99.99', $d->jsonSerialize());
        $this->assertSame('"99.99"', \json_encode($d));
    }

    public function testFormat(): void
    {
        $d = new Decimal('1234567.891');
        $this->assertSame('1,234,567.89', $d->format(2, '.', ','));
        $this->assertSame('1.234.567,89', $d->format(2, ',', '.'));
        $this->assertSame('1234567.89', $d->format(2, '.', ''));
    }

    // -------------------------------------------------------
    // Static helpers
    // -------------------------------------------------------

    public function testZero(): void
    {
        $z = Decimal::zero(3);
        $this->assertTrue($z->isZero());
        $this->assertSame(3, $z->scale);
        $this->assertSame('0.000', $z->value);
    }

    public function testOne(): void
    {
        $o = Decimal::one(2);
        $this->assertTrue($o->equals('1'));
        $this->assertSame(2, $o->scale);
        $this->assertSame('1.00', $o->value);
    }

    public function testMin(): void
    {
        $result = Decimal::min(
            new Decimal('5'),
            new Decimal('3'),
            new Decimal('9'),
        );
        $this->assertTrue($result->equals('3'));
    }

    public function testMax(): void
    {
        $result = Decimal::max(
            new Decimal('5'),
            new Decimal('3'),
            new Decimal('9'),
        );
        $this->assertTrue($result->equals('9'));
    }

    public function testSum(): void
    {
        $result = Decimal::sum(
            new Decimal('1'),
            new Decimal('2'),
            new Decimal('3'),
        );
        $this->assertTrue($result->equals('6'));
    }

    public function testAvg(): void
    {
        $result = Decimal::avg(
            new Decimal('2'),
            new Decimal('4'),
            new Decimal('6'),
        );
        $this->assertTrue($result->equals('4'));
    }

    public function testMinEmptyThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        Decimal::min();
    }

    public function testMaxEmptyThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        Decimal::max();
    }

    public function testSumEmptyThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        Decimal::sum();
    }

    public function testAvgEmptyThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        Decimal::avg();
    }

    // -------------------------------------------------------
    // Immutability
    // -------------------------------------------------------

    public function testImmutability(): void
    {
        $original = new Decimal('10.00');
        $added = $original->add('5');
        $subtracted = $original->subtract('3');

        $this->assertSame('10.00', $original->value);
        $this->assertTrue($added->equals('15.00'));
        $this->assertTrue($subtracted->equals('7.00'));
        $this->assertNotSame($original, $added);
        $this->assertNotSame($original, $subtracted);
    }

    // -------------------------------------------------------
    // Backend
    // -------------------------------------------------------

    public function testGetBackendName(): void
    {
        $name = Decimal::getBackendName();
        $this->assertIsString($name);
        $this->assertNotEmpty($name);
    }

    public function testSetBackend(): void
    {
        $originalBackendName = Decimal::getBackendName();

        try {
            $mock = new class implements DecimalBackend {
                public function add(string $a, string $b, int $scale): string
                {
                    return '0';
                }

                public function subtract(string $a, string $b, int $scale): string
                {
                    return '0';
                }

                public function multiply(string $a, string $b, int $scale): string
                {
                    return '0';
                }

                public function divide(string $a, string $b, int $scale): string
                {
                    return '0';
                }

                public function modulo(string $a, string $b, int $scale): string
                {
                    return '0';
                }

                public function power(string $base, int $exponent, int $scale): string
                {
                    return '0';
                }

                public function sqrt(string $value, int $scale): string
                {
                    return '0';
                }

                public function compare(string $a, string $b, int $scale): int
                {
                    return 0;
                }

                public function negate(string $value): string
                {
                    return '0';
                }

                public function abs(string $value): string
                {
                    return '0';
                }

                public function getName(): string
                {
                    return 'mock-test-backend';
                }
            };

            Decimal::setBackend($mock);
            $this->assertSame('mock-test-backend', Decimal::getBackendName());
        } finally {
            if (\extension_loaded('decimal')) {
                Decimal::setBackend(new \Tyhp\Backend\PhpDecimalBackend());
            } elseif (\extension_loaded('bcmath')) {
                Decimal::setBackend(new \Tyhp\Backend\BcMathBackend());
            } elseif (\extension_loaded('gmp')) {
                Decimal::setBackend(new \Tyhp\Backend\GmpBackend());
            } else {
                Decimal::setBackend(new \Tyhp\Backend\IntegerScaledBackend());
            }
        }
    }

    // -------------------------------------------------------
    // Factory function
    // -------------------------------------------------------

    public function testDecimalFactory(): void
    {
        $d = \Tyhp\decimal('42.5');
        $this->assertInstanceOf(Decimal::class, $d);
        $this->assertTrue($d->equals('42.5'));
    }

    // -------------------------------------------------------
    // Edge cases
    // -------------------------------------------------------

    public function testVeryLargeNumber(): void
    {
        if (!\extension_loaded('bcmath')) {
            $this->markTestSkipped('Requires bcmath extension for precision tests');
        }

        $large = new Decimal('99999999999999999999999999999');
        $result = $large->add('1');
        $this->assertTrue($result->equals('100000000000000000000000000000'));
    }

    public function testNegativeZero(): void
    {
        $d = new Decimal('-0');
        $this->assertTrue($d->isZero());
        $this->assertFalse($d->isNegative());
    }

    public function testArithmeticPreservesScale(): void
    {
        if (!\extension_loaded('bcmath')) {
            $this->markTestSkipped('Requires bcmath extension for precision tests');
        }

        $a = new Decimal('1.00');
        $b = new Decimal('2.0000');
        $result = $a->add($b);
        $this->assertGreaterThanOrEqual(2, $result->scale);
    }

    public function testChainedOperations(): void
    {
        $result = (new Decimal('100'))
            ->add('50')
            ->subtract('25')
            ->multiply('2')
            ->divide('5');

        $this->assertTrue($result->equals('50'));
    }
}
