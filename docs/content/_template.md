---
title: 'Title of the item'
status:
  tier: 0
  story: '08'
  state: complete
---

```status
tier: 1
stories:
  - '11'
  - '16'
state: in-progress
```

## Normal text:

This is a text block to show in a <p></p> tag.  This supports full html if allowHtml==true.

## Callout boxes:

:::note
This is a text block to show a **note** box (usually blue or light blue).  This supports full html if allowHtml==true.
:::

:::tip
This is a text block to show a **tip** box (usually green).  This supports full html if allowHtml==true.
:::

:::warning
This is a text block to show a **warning** box (usually yellow).  This supports full html if allowHtml==true.
:::

:::danger Deprecation Notice
This is a text block to show a **danger** box (usually red).  This supports full html if allowHtml==true.
:::

## Alert boxes:

:::alert{style=primary}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=secondary}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=success}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=danger}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=warning}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=info}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=light}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

:::alert{style=dark}
This is a alert box to show text.  This supports full html if allowHtml==true.
:::

## Bulleted list:

- each item
- on this list
- is a bullet item
- This supports full html if allowHtml==true.

## Numered List:

1. each item
2. on this list
3. is a numbered item
4. This supports full html if allowHtml==true.

## Tyhp code:

```tyhp
<?tyhp
  // each line
  // on this list
  // is a line of Tyhp code

int $myInt = 10;
$otherInt = $myInt - 1;
```

## PHP Code:

```php
<?php
// each line
// on this list
// is a line of PHP code

$myInt = 10;
$otherInt = $myInt - 1;
```

## JSON Code:

```json
{
    "asdf": "asdf"
}
```

## Class def:

```classdef
modifiers: final
type: class
identifier: MyClass
generics:
  -
    identifier: TType
    extends: int|string
extends: OtherClass<TType>
implements:
  - OtherInterface<TType>
members:
  -
    type: comment
    content: Constants
  -
    type: const
    def:
      modifiers: public
      type: string
      identifier: GREEN
      value: "'#00AA00'"
  -
    type: newLine
  -
    type: comment
    content: Properties
  -
    type: property
    def:
      modifiers: 'public readonly'
      type: int
      identifier: $red
      value: "'#FF0000'"
  -
    type: propertyAccessor
    def:
      modifiers: public
      type: int
      identifier: $blue
      accessors:
        - get
        - 'protected set'
  -
    type: newLine
  -
    type: comment
    content: Methods
  -
    type: method
    def:
      modifiers: public
      identifier: getColor
      generics:
        -
          identifier: TColorType
          extends: Color|string
      parameters:
        -
          type: '?TColorType'
          identifier: $from
          value: 'null'
      returnType: string
  -
    type: newLine
  -
    type: comment
    content: 'Operator Overrides'
  -
    type: operator
    def:
      identifier: +
      parameters:
        -
          type: MyClass
          identifier: $a
        -
          type: MyClass
          identifier: $b
      returnType: MyClass
  -
    type: newLine
  -
    type: comment
    content: 'Enum Cases'
  -
    type: enumCase
    def:
      identifier: YELLOW
      value: "'#FFFF00'"
  -
    type: newLine
  -
    type: comment
    content: 'Type Aliases'
  -
    type: typeAlias
    def:
      modifiers: public
      identifier: myType
      generics:
        -
          identifier: TOther
      value: 'OtherClass<TType, TOther>|null'
```

## Function def:

```functiondef
modifiers: 'public static async'
identifier: functionName
parameters:
  -
    type: TypeOfParam
    identifier: $paramName
  -
    type: '?OtherTypeOfParam'
    identifier: $paramName2
    value: 'null'
returnType: void
```

## Member def:

:::member[$paramName]
This param is a param that is used as a param when a param is needed as a param.  "identifier" can be anything.
:::

## Include shared content:
