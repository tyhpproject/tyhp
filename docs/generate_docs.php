#!/usr/bin/env php
<?php

chdir(__DIR__);

require __DIR__ . '/vendor/autoload.php';

(new Tyhp\Docs\SiteBuilder(__DIR__))->build();
