<?php

// This is code from a file

class MyClass extends BaseClass
{
    private bool $__result;

    public function __constuct(TType $key)
    {
        parent::__constuct($key);
        // blah
        
    }

    public function getResult(): bool
    {
        return $this->__result;
    }

    public function setResult(bool $value): static
    {
        $this->__result = $value;
        return $this;
    }
}