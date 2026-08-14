# Checker

## things to check (in to particular order)
- throw can only throw an instance of \Throwable
- catch can catch a type alias, but only a single type or union type ... no intersection types!!!
- catch can only catch real object types, not scalar/struct/enum types
- catch can only catch types that extend \Throwable either directly or indirectly
- variable types, assignment and usage
- returns can only return the return type
- function/methods with non-void return must return type in all paths
- logical statements (if, while, etc.) can only be bool (this is different than php)
- variable assignment before use
- override/implemented method is compatible with abstract/parent/interface
- Closure instance in class binding.  It is bound to the containing class unless \Closure::bind() is called and the result of that is a new closure with a new binding.
- so much more!!!!!