<?php

namespace TestData\Php;

$value = 0;

if ($value < 0) {
    echo 'negative';
}

for ($i = 0; $i < $value; $i++) {
    if ($i > 10) {
        break;
    }
}

foreach ([1, 2, 3] as $item) {
    if ($item === 2) {
        continue;
    }
}

while ($value > 0) {
    $value--;
}

switch ($value) {
    case 0:
        echo 'done';
        break;
    default:
        echo 'other';
}
