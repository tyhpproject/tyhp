---
title: TypeScript
---

TypeScript is the closest analogy to what Tyhp does and serves as a direct inspiration for the project. TypeScript adds a type system on top of JavaScript and compiles to standard JavaScript. Tyhp does the same for PHP. The relationship can be expressed as: TypeScript is to JavaScript what Tyhp is to PHP.

## What TypeScript Is

TypeScript is a strongly-typed superset of JavaScript developed by Microsoft. It adds static type checking, interfaces, generics, type inference, and other type system features to JavaScript. TypeScript compiles (transpiles) to standard JavaScript, erasing all type information at compile time. Since its introduction in 2012, TypeScript has become one of the most widely adopted programming languages, demonstrating that a typed superset approach can succeed at massive scale.

## Similarities

Tyhp follows the TypeScript model closely in philosophy and approach:

- Both are strongly-typed supersets of a dynamically-typed language
- Both transpile to the original language — TypeScript to JavaScript, Tyhp to PHP
- Both erase generic type information at compile time (type erasure)
- Both support generics with type parameters and constraints
- Both support type aliases, union types, and intersection types
- Both support type inference, reducing the need for explicit annotations in many cases
- Both aim to produce readable, idiomatic output code
- Both allow gradual adoption in existing codebases
- Both use declaration files for describing external libraries (TypeScript uses .d.ts files, Tyhp uses Tyhpdef files)
- TypeScript ships a mature Language Server; Tyhp's LSP is planned (Story 19) and is not in this alpha.

## Differences

While Tyhp follows the TypeScript model, the differences between PHP and JavaScript lead to meaningful differences between Tyhp and TypeScript:

- Tyhp targets PHP, TypeScript targets JavaScript — different runtime environments, ecosystems, and deployment models.
- PHP already has more runtime type checking than JavaScript (type declarations on function parameters, return types, and properties), so Tyhp builds on that existing foundation rather than starting from zero.
- TypeScript uses structural typing (if two types have the same shape, they are compatible). Tyhp primarily uses nominal typing consistent with PHP's type system (two types must share an inheritance or implementation relationship to be compatible), though structs use structural compatibility.
- Tyhp adds features that TypeScript does not need because PHP lacks them at the language level: operator overloads, conversion operators, extension methods, structs (backed by associative arrays), and property accessors.
- TypeScript's ecosystem is significantly more mature — it has over a decade of development, extensive tooling, and widespread industry adoption. Tyhp is in earlier stages of development.
- Tyhp supports PHP-specific constructs like traits, enums with backing types, namespaces, and the PHP standard library.
- TypeScript has a larger team and broader community. Tyhp is a smaller project focused specifically on the PHP ecosystem.

## The TypeScript Model

TypeScript's success has proven that a typed superset approach works. It demonstrated that developers will adopt gradual typing when the tooling is good, the migration path is smooth, and the output code is readable. Tyhp aims to bring these same benefits to the PHP ecosystem — not by copying TypeScript feature-for-feature, but by applying the same philosophy to PHP's unique strengths and constraints.
