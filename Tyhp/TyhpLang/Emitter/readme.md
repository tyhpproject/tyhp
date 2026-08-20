# Emitter

The process should go as follows:
- reduce aliases in the ast tree (may be a binder thing?)
    - tyhpdef aliases
    - type aliases
    - property accessors
    - trait adaptations
    - structs
- get file info per tyhp file produce `TyhpInputInfo`
    - declarations (not output file)
    - imports
    - namespace (file level)
- get file code and namespace blocks and treat as standalone file with tyhp file info, produce `TyhpInputSource` which has `TyhpFileInfo` as property
- foreach TyhpFileSource block produce `PHPOutputSource` which has `TyhpFileInfo` as property
    - split out object declarations into each
    - split out output_file declarations into each
    - split out root level code into each
- combine like root level code instances
- foreach PHPOutputSource
    - reduce declares
    - reduce imports
    - generate php file (ast to PHP, including optimizations)
    - path+filename is either PSR4 object name or output_file="" value


Tyhp code can contain special docblock tags for directing the emitter.
- `@tyhpEmitterStart(onCall)`
    - This is uses to start a block of code to use as an emitter template for a method or function.
    - This block of code will be using to emit PHP when the function or method is called.
    - Only valid on a function or method declaration (including interfaces, classes, traits, etc.)
    - If this is a method and it is overridden, the overridden method does not automatically inherit this tag
    - valid actions/items:
        - `/@@ inheritFrom([symbol path to inherit this same block from]) @@/`
            - example: `/@@ inheritFrom(\Tyhp\Disposable::__using) @@/` - would use use the template from this docblock
            - if used, this must be the only item in the `@tyhpEmitterStart(onCall)` block
            - this will produce a compile time error if the source template is not defined or if the source template is incompatible
        - `/@@ $[template variable name] @@/`
            - this will output the value stored in the template variable.
            - All parameters of the function/method will be defined as template variables and will contain the code used to specify it as an argument.
            - example: `/@@ $disposable @@/` - this may output something like: `$mayVar = new MyDisposableObj()`, if the method declaration was `public static function __using(Disposable $disposable): bool` and it was called like so: `Disposable::__using($mayVar = new MyDisposableObj());`
            - for this block, the function/method arguments are the only pre-defined template variables
            - also, the parameters of the method can be referenced using indexes
                - this is done by specifying a number after the `$` instead of the parameter name like so: `$0`, `$1`, `$2`, and so on.
            - for variadic parameters the `$*` template variable can be used to get all of the arguments
        - `/@@ generateVariableName([string: variable name prefix, can be a blank string], $[output template variable to assign to]) @@/`
            - generate a unique variable name to use in the code
            - the prefix is optional, and a blank string can be specified if no prefix is desired.
            - this dynamically generated variable name can be referenced using the second output
            - example: `/@@ generateVariableName("__disposable", $disposableVar) @@/` - would output something similar to `$__disposable23kjgcasdf234jk23vgasdvg3`, then we can use the `$disposableVar` template variable
        - `/@@ callingScopeCodeBlockEmit() @@/`
            - the calling code block scope for where this function was called from.
            - Useful for if you want to wrap the calling scope in another block like a `try..finally`
            - this starts at the point of when the function/method was called and finishes when the scope returns to a parent block
        - `/@@ setTemplateVariableStart([output template variable]) @@/`
            - set the value of a template variable
            - if you set a template variable that is an argument passed in, it will replace the output code it would have used for that argument
            - you can reference the original value of the template variable from within the block, if it is undefined, it will be empty.
            - when you reference it in the block and it is for an argument, the value is for what Tyhp would have originally emitted for it.  This is useful to prepend an attribute, or wrap it in a call or similar.
        - `/@@ setTemplateVariableEnd() @@/`
            - ends setting the template variable
        - `/@@  @@/`
        - `/@@  @@/`
        - `/@@  @@/`
        - `/@@  @@/`
        - `/@@  @@/`
        - `/@@  @@/`
- `@tyhpEmitterEnd([optional signed signature])`
    - this is the ending tag to close the template block
    - if this is not present then the template will continue to the end of the docblock
    - there is an optional signed signature for this block
        - this ensures that the block is not modified and is from a trusted source
        - to trust template blocks you must add the signer's signing key to your Tyhp config, otherwise you will get compile time warnings that the PHP code is emitted using an unsigned template
        - the signature identifies the signing certificate used to validate the signature, and the signature value itself
        - the developer should keep their signing keys secret








## Tyhp code with template:
```PHP
namespace Tyhp;

interface IsDisposable {
    public function dispose();
}

interface AsyncIsDisposable {
    public function disposeAsync();
}

final class DisposableHelper {
    /**
     * A method for using a disposable object.
     *
     * @param array<IsDisposable|AsyncIsDisposable|null> $disposables
     * @return true
     * 
     * @tyhpEmitterStart(onCall)
     * /@@ setTemplateVariableStart($*) @@/
     * ...(/@@ generateVariableName("disposable", $disposableVar) @@/ = [ /@@ $* @@/ ]))
     * /@@ setTemplateVariableEnd() @@/
     * try {
     *     /@@ callingScopeCodeBlockEmit() @@/;
     * } finally {
     *     \Tyhp\DisposableHelper::dispose(...(/@@ $disposableVar @@/ ?? []));
     * }
     * @tyhpEmitterEnd("cert_id_234234134145:asdf1234asdf1234asdf1234asdf1234asdf1234")
     */
    final public static function using(IsDisposable|AsyncIsDisposable|null ...$disposables): true {
        return true;
    }

    final public static function dispose(mixed ...$disposables): Promise
    {
        $promises = [ Promise::resolve() ];

        foreach ($disposables as $disposable) {
            if ($disposable instanceof IsDisposable) {
                $disposable->dispose();
                $promises[] = Promise::resolve();
            }
            
            if ($disposable instanceof AsyncIsDisposable) {
                $promises[] = $disposable->disposeAsync();
            }
        }

        return Promise::all($promises);
    }
}

/**
 * @param array<IsDisposable|AsyncIsDisposable|null> $disposables
 * @return true
 * 
 * @tyhpEmitterStart(onCall)
 * /@@ inheritFrom(DisposableHelper::using) @@/
 * @tyhpEmitterEnd
 */
function using(IsDisposable|AsyncIsDisposable|null ...$disposables): true {
    return DisposableHelper::using(...$disposables);
}
```

## Tyhp code that uses the method:
```PHP

use function Tyhp\using;

if (using($myVar = MyDisposableObj::create())) {
    $myVar->throwIfInvalid();
} else {
    // not able to get in instance of the disposable obj
    $alertService->error("Cannot get instance");
}

// ... other code that should not see $myVar

if ($otherCondition) {
    if (!DbConnection::IsConfigured()) {
        $alertService->error("DB not configured");
        exit 1;
    }

    using(
        $conn = new DbConnection("client123"),
        $table = $conn->table('registered_families')
    );

    $values = $table->select(['*'], ["last_name LIKE '%ston'"]);

    foreach ($values as $v) {
        if (MyDbValidationService::isValidFamily($v)) {
            // the MyDbValidationService::isValidFamily method is a type guard that verifies that it is a struct type
            $alertService->info("Found last name: " . $v->last_name);
        }
    }

    unset($values);
} else {
    $alertService->error("Condition is FALSE");
}


```

## Emitted PHP code:
```PHP
use function Tyhp\using;

if (using(...($__disposable4jkv6kkc34ckakjc = [ $myVar = new MyDisposableVar()] ))) {
    try {
        $myVar->throwIfInvalid();
    } finally {
        \Tyhp\DisposableHelper::dispose(...($__disposable4jkv6kkc34ckakjc ?? []));
    }
} else {
    // not able to get in instance of the disposable obj
    $alertService->error("Cannot get instance");
}

// ... other code that should not see $myVar

if ($otherCondition) {
    if (!DbConnection::IsConfigured()) {
        $alertService->error("DB not configured");
        exit 1;
    }

    using(...($__disposable2n5ers4iu7ou7i34 = [ $conn = new DbConnection("client123"), $table = $conn->table('registered_families') ]));
    try {
        $values = $table->select(['*'], ["last_name LIKE '%ston'"]);

        foreach ($values as $v) {
            if (MyDbValidationService::isValidFamily($v)) {
                // the MyDbValidationService::isValidFamily method is a type guard that verifies that it is a specific struct type
                $alertService->info("Found last name: " . $v->last_name);
            }
        }

        unset($values);
    } finally {
        \Tyhp\DisposableHelper::dispose(...($__disposable2n5ers4iu7ou7i34 ?? []));
    }
} else {
    $alertService->error("Condition is FALSE");
}
```

## Known Limitations (Story 09)

After Story 09, the emitter can produce basic PHP from Tyhp and round-trip standard PHP sources. The following features are **fully emittable**:

- Class, interface, trait, and enum declarations (generics stripped from signatures)
- Function and method declarations (Tyhp-only types stripped)
- PHP-compatible statements and expressions
- Import/use statements (`import` → PHP `use`)
- Type guard return types (`$param is Type` → `bool`)
- `nameof()` → string literal (basic cases)
- `default()` → literal values for basic types
- `typeof()` → `\Tyhp\Type` factory / generic lookup
- `variable_exists($v)` → `\array_key_exists('v', \get_defined_vars())`
- Tyhp magic constants (`__TYHP_*`) → PHP equivalents

The following require **Story 11** (`PLACEHOLDER_STORY_11` markers in emitter code):

| Feature | Location |
|---------|----------|
| Property accessors (PHP &lt; 8.4) | `AliasConverter.cs` |
| User-defined operator overload rewriting | `AliasConverter.cs` |
| Struct emission (construction, `with`, clone, custom backing) | `StructEmissionHelper.cs` / `AliasConverter.cs` |
| Async/await → Promise/Fiber | `AliasConverter.cs`, `TyhpEmitter.Declarations.cs` |
| Disposable → scope-based auto-dispose | `AliasConverter.cs` |
| Function overload dispatch | `AliasConverter.cs` |
| Object `with` keyword (ObjectHelper / PHP 8.5 `clone()` / stmt expand) | `WithKeywordHelper.cs` / `AliasConverter.cs` |
| Runtime generic tracking (`typeof(T)`, `GenericObject`) | `TyhpEmitter.Expressions.cs` |
| `using` block transformation | `TyhpEmitter.Statements.cs` |

**Done in Story 11 (no longer placeholders):** extension-method call-site rewriting (single, chained, nullable, and scalar receivers) via `AliasConverter.TryRewriteExtensionMethodCall`.

**Deferred to other stories:**

- Authoritative disk writing, clean/dry-run modes → Story 10 (`OutputWriterService`)
- Source maps → Story 17 (`SourceMapWriter` + `OutputWriterService`; validator is a later phase)

**Removed from design:** `@tyhpEmitterStart` / `@tyhpEmitterEnd` emitter template system.

Tyhp-specific constructs encountered during emission produce `EmitterTyhpConstructNotImplemented` warnings rather than crashing the compiler.

**Note:** `Examples/*.php` files in the repository are reference/output samples using PHP 8.4 syntax; many do not parse cleanly in Tyhp's PHP parse mode today. End-to-end validation uses `Examples/*.tyhp` sources and Tyhp parse mode for round-trip checks.