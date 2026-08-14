---
title: Bref
---

Bref is an open-source tool for running PHP applications on AWS Lambda. It is sometimes mentioned alongside Tyhp, but the two projects address entirely different concerns and are complementary rather than alternatives.

## What Bref Is

Bref provides PHP runtimes and tooling for deploying PHP applications as serverless functions on AWS Lambda. It handles the infrastructure layer — packaging PHP applications, providing Lambda-compatible PHP runtimes, integrating with API Gateway, SQS, and other AWS services, and managing deployment via the Serverless Framework or AWS SAM. Bref is a deployment and infrastructure tool, not a language or compiler.

## Different Layers, Different Purposes

Tyhp and Bref operate at completely different layers of the stack:

- Tyhp is a language and compiler that adds strong typing and modern language features to PHP. It operates at the source code level.
- Bref is a deployment tool that runs PHP on serverless infrastructure. It operates at the infrastructure and runtime level.
- Tyhp compiles to standard PHP. Bref runs standard PHP. They do not overlap or conflict.

## Using Tyhp with Bref

Because Tyhp compiles to standard PHP, using Tyhp with Bref is straightforward:

1. Write your application in Tyhp with full type safety and modern language features.
2. Compile your Tyhp code to PHP using the Tyhp compiler.
3. Deploy the compiled PHP code to AWS Lambda using Bref, exactly as you would deploy any PHP application.
4. Optionally, describe Bref's PHP libraries in Tyhpdef files for type-safe usage within your Tyhp code.

No special integration is required. Tyhp's compiled output is standard PHP, so any tool that runs PHP — including Bref — works without modification.
