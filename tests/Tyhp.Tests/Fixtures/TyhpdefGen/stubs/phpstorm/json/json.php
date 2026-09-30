<?php

/**
 * STUB-ONLY SUMMARY MUST NOT APPEAR
 * @param mixed $value
 * @return string|false a JSON encoded string
 */
function json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false {}

/**
 * @param string $json
 * @return mixed
 */
function json_decode(string $json, ?bool $associative = null, int $depth = 512, int $flags = 0): mixed {}

interface JsonSerializable
{
    /**
     * @return mixed
     */
    public function jsonSerialize(): mixed;
}

class JsonException extends Exception
{
    /**
     * @var int
     */
    protected $code;
}
