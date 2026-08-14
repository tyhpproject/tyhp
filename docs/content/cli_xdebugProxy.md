---
title: 'CLI: XDebug Proxy'
status:
  tier: 2
  story: '18'
  state: planned
---

:::warning Not in this alpha
This feature is **not included** in Tyhp 805.0.0-alpha.1 (roadmap Tier 2/3). The rest of this page describes the planned design. Do not expect these commands or syntax to work yet.
:::


The xdebug_proxy action starts a proxy server that sits between your IDE's debugger and PHP's XDebug extension. It uses source maps to translate between compiled PHP file paths and line numbers and their corresponding Tyhp source locations, allowing you to debug your original .tyhp source files while PHP executes the compiled output.

## Usage

```
tyhp xdebug_proxy [options]
```

## How It Works

The XDebug proxy operates as a bidirectional message relay using the DBGp protocol. It opens two TCP listening ports:

- IDE port (default 9003) — Your IDE's debug adapter connects here instead of connecting directly to XDebug.
- XDebug port (default 9004) — XDebug connects here when a PHP script starts a debug session.

When both sides connect, the proxy pairs them into a debug session and relays all DBGp messages between them. For each message, it inspects the content and translates file paths and line numbers using the source maps generated during tyhp build.

## Translation Details

The proxy performs the following translations:

- Breakpoints — When you set a breakpoint on line 42 of User.tyhp, the proxy translates it to the corresponding line in User.php before forwarding to XDebug.
- Stack traces — When XDebug reports a stack frame at line 67 of User.php, the proxy maps it back to line 42 of User.tyhp before forwarding to your IDE.
- Source retrieval — When your IDE requests the source of a file, the proxy returns the original .tyhp content instead of the compiled PHP.
- Breakpoint responses — All breakpoint-related responses are translated so your IDE shows Tyhp file paths and line numbers.

Files without source maps (such as third-party PHP libraries) pass through untranslated — their paths and line numbers appear as-is from XDebug.

## Prerequisites

- XDebug PHP extension installed and configured on your PHP runtime
- A built Tyhp project with source maps enabled (set build.generateSourcemap to true in tyhp.json, then run tyhp build)
- An IDE with XDebug debugging support (VS Code with PHP Debug extension, PhpStorm, etc.)

## Options

- --ide-port=<port> — Port for IDE debug adapter connections (default: 9003).
- --xdebug-port=<port> — Port for XDebug connections (default: 9004).
- --sourcemap-dir=<path> — Directory containing .php.map source map files. Defaults to the project's output.path.
- --ide-key=<key> — Only accept XDebug sessions with this IDE key. If not set, all sessions are accepted.
- --log-level=<level> — Logging verbosity: debug, info, warn, or error (default: info).

## Setup

To use the XDebug proxy, configure XDebug to connect to the proxy's XDebug port instead of directly to your IDE, and configure your IDE to connect to the proxy's IDE port.

## Step 1: Build with Source Maps

Enable source map generation in your tyhp.json and build your project:

```json
{
    "build": {
        "generateSourcemap": true
    }
}
```

```
tyhp build
```

## Step 2: Configure XDebug

In your php.ini or xdebug.ini, point XDebug at the proxy's XDebug port:

```
xdebug.mode = debug
xdebug.client_host = 127.0.0.1
xdebug.client_port = 9004
xdebug.idekey = tyhp
```

## Step 3: Start the Proxy

```
tyhp xdebug_proxy
```

The proxy displays the listening ports and the number of source map files loaded:

```
XDebug Proxy started
  IDE port:      9003
  XDebug port:   9004
  Sourcemaps:    42 files loaded from ./build/
  Source root:   ./src/
  IDE key:       (any)
```

## Step 4: Configure Your IDE

Configure your IDE's debugger to connect to the proxy's IDE port (9003 by default). For VS Code, add a launch.json configuration:

```json
{
    "name": "Debug Tyhp (via proxy)",
    "type": "php",
    "request": "launch",
    "port": 9003
}
```

## tyhp.json Configuration

Proxy settings can also be configured in the xdebugProxy section of tyhp.json:

```json
{
    "xdebugProxy": {
        "idePort": 9003,
        "xdebugPort": 9004,
        "ideKey": null,
        "maxSessions": 10,
        "logLevel": "info",
        "autoReloadSourceMaps": true
    }
}
```

:::tip
Set logLevel to "debug" to see every DBGp message flowing through the proxy, including all path translations. This is helpful for troubleshooting breakpoint mapping issues.
:::

:::note
The XDebug proxy is a long-running process. Use Ctrl+C to stop it gracefully. It will close all active debug sessions and release the listening ports.
:::
