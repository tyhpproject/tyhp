---
title: ReactPHP
---

ReactPHP is an event-driven, non-blocking I/O library for PHP. It is sometimes mentioned alongside Tyhp because both deal with asynchronous programming in PHP, but they operate at entirely different levels and solve different problems.

## What ReactPHP Is

ReactPHP provides an event loop and asynchronous I/O primitives for PHP. It enables non-blocking network operations, timers, streams, HTTP servers, and more — all within standard PHP. ReactPHP is a runtime library distributed as a collection of Composer packages, not a language or compiler. It uses callbacks, Promises, and (in newer versions) integration with PHP Fibers to handle asynchronous operations.

## How They Differ

Tyhp and ReactPHP solve async programming at different layers:

- Tyhp is a language and compiler. Its async/await syntax compiles to PHP that uses the **`tyhp/async`** runtime (Fibers and a `stream_select` event loop inspired by libraries like ReactPHP).
- ReactPHP is a runtime library. It provides an event loop, I/O drivers, and networking for plain PHP.
- You can still call ReactPHP from Tyhp if you describe it in tyhpdef files. Tyhp’s compiled `async`/`await` does **not** require ReactPHP and does not emit ReactPHP loop calls by default.

## Using Them Together

Tyhp and ReactPHP are complementary rather than competing technologies:

- Describe ReactPHP's classes and interfaces in tyhpdef files to get type checking when you call ReactPHP from Tyhp.
- Use Tyhp's async/await with `tyhp/async` for typed `Promise<T>` without taking a ReactPHP dependency.
- You can mix both in one process if you want ReactPHP I/O alongside Tyhp-compiled modules — that is an integration choice, not the default emit.

:::note
ReactPHP is not the only async runtime compatible with Tyhp. Any PHP library that works with Fibers or provides an event loop can serve as the runtime backing for Tyhp's compiled async code.
:::
