---
title: ReactPHP
---

ReactPHP is an event-driven, non-blocking I/O library for PHP. It is sometimes mentioned alongside Tyhp because both deal with asynchronous programming in PHP, but they operate at entirely different levels and solve different problems.

## What ReactPHP Is

ReactPHP provides an event loop and asynchronous I/O primitives for PHP. It enables non-blocking network operations, timers, streams, HTTP servers, and more — all within standard PHP. ReactPHP is a runtime library distributed as a collection of Composer packages, not a language or compiler. It uses callbacks, Promises, and (in newer versions) integration with PHP Fibers to handle asynchronous operations.

## How They Differ

Tyhp and ReactPHP solve async programming at different layers:

- Tyhp is a language and compiler. Its async/await syntax is a language-level feature that compiles to PHP code using Fibers. It provides syntactic sugar and compile-time type safety for asynchronous operations.
- ReactPHP is a runtime library. It provides the actual event loop, I/O drivers, and networking infrastructure that makes non-blocking operations possible.
- Tyhp's async/await provides a clean, linear coding style for async operations with compile-time type checking via Promise<T>. ReactPHP provides the underlying runtime machinery that drives those operations.
- ReactPHP works with plain PHP code. Tyhp's async features compile down to plain PHP code.

## Using Them Together

Tyhp and ReactPHP are complementary rather than competing technologies. Tyhp's compiled async code can use ReactPHP as its event loop and I/O layer:

- Describe ReactPHP's classes and interfaces in Tyhpdef files to get full type safety when using ReactPHP from Tyhp code.
- Use Tyhp's async/await syntax to write clean, readable asynchronous code that compiles to PHP utilizing ReactPHP's event loop under the hood.
- Tyhp adds type-safe Promise<T> with generic type parameters, providing compile-time guarantees that ReactPHP's untyped Promises in plain PHP cannot offer.

:::note
ReactPHP is not the only async runtime compatible with Tyhp. Any PHP library that works with Fibers or provides an event loop can serve as the runtime backing for Tyhp's compiled async code.
:::
