# RDMD — Raisin Docs Markdown

A superset of CommonMark designed for intuitive editing. Files use the `.rdmd` extension. The parser builds on the existing CommonMark/GFM parser, producing the same `ParsedBlock` output so all downstream rendering, editing, and export infrastructure is shared.

Standard `.md` files continue to use CommonMark/GFM rules. The format is selected by file extension.

## Design principles

- A newline is a newline. No soft breaks, no continuation lines.
- Spaces mean spaces. No silent trimming.
- If you want one long line, write one long line — word wrap handles the rest.
- Markdown-compatible where it doesn't conflict with the above.

## Line breaks and paragraphs

### Newline = line break

Every newline in the source produces a line break in the output. There is no soft break concept. CommonMark's two-space / backslash hard break syntax is unnecessary and ignored.

```
This is line one.
This is line two.
```

Renders as two separate lines, not a joined paragraph.

### Paragraphs

A blank line separates paragraphs, same as CommonMark.

```
This is a paragraph.

This is another paragraph.
```

## Whitespace and indentation

### Leading spaces are preserved

Indentation is not silently stripped. Leading spaces in a line are rendered as-is.

```
   This line is indented three spaces.
    This line is indented four spaces.
```

### TAB

TAB means "indent one level." What it does depends on context:

- **TAB + list marker** → nested list item (`\t- child`)
- **TAB + no marker** → indented content (`\tsome text`)
- **TAB after blank line in a list** → continues the current list item as a new paragraph

Tab display width: default 4, configurable (global setting).

### Spaces

Spaces are visual only — preserved as-is, no structural meaning.

### No indented code blocks

The CommonMark rule "4 spaces of indentation = code block" is removed. Since leading spaces are preserved and meaningful, this rule would conflict. Use fenced code blocks (`` ``` ``) exclusively.

## Lists

### List continuation

Lines following a list item belong to that item until the next list marker or a blank line.

Continuation lines always render at the item's content level (aligned with the item text, after the bullet/number). TAB adds extra indentation from there.

```
- Item text
continuation (renders aligned with "Item text")
	indented continuation (renders one level deeper than "Item text")
		double indented (two levels deeper)
```

### Paragraphs within list items

A blank line normally ends a list. However, a TAB on the line after the blank line keeps the content within the current list item as a new paragraph:

```
1. First paragraph of item one.

	Second paragraph, still in item one (TAB keeps it in the item).

	   Indented second paragraph (TAB + spaces).

2. Item two.

This is a new paragraph, not part of the list (no TAB after blank line).
```

Rules:
- Blank line + TAB → new paragraph within the current list item
- Blank line + no TAB → list ends, regular paragraph follows

### List nesting

One TAB per nesting level:

```
- Parent
	- Child
		- Grandchild
```

Works the same for all list types:

```
1. First
	1. Sub-one
	2. Sub-two
		- Mixed nesting
2. Second
```

### Sub-numbered ordered lists

Sub-numbering uses hierarchical markers in the source. The trailing dot is required to distinguish from regular text:

```
1. First
2. Second
	2.1. Sub-one
	2.2. Sub-two
		2.2.1. Deep item
3. Third
```

The editor auto-maintains all numbers on insert, delete, and reorder.

## Blockquotes

Every line in a blockquote must have the `>` prefix. No lazy continuation.

```
> Line one.
> Line two.
```

A line without `>` ends the blockquote.

## Inline formatting

### Formatting across line breaks

Inline formatting spans across line breaks. Both lines render with the style applied.

```
This is **bold
text** here.
```

Renders as two lines, both bold between the delimiters.

## Escaping

A `\` at the start of a line prevents the next token from being parsed as a block marker:

```
\1. Not a list.
\2.1. Also not a list.
\3.14 is approximately pi.
\- Not a bullet.
\> Not a blockquote.
```

## Extended features

### Numbered headings

Numbered headings work like sub-numbered lists — the numbering is in the source and the editor auto-maintains it. A heading without a number prefix is not numbered. Numbering is opt-in per heading.

```
# 1 Introduction
## 1.1 Background
## 1.2 Scope
# 2 Architecture
## 2.1 Overview
## This heading is not numbered
# 3 Implementation
```

No trailing dot on heading numbers (unlike lists) — the `#` prefix already prevents ambiguity.

The space after `#` is optional:

```
#This is a Heading1
# This is also a Heading1
```

Rules:
- A heading with a number prefix (e.g. `# 1`) is numbered. The editor renumbers on insert/delete/reorder.
- A heading without a number prefix is not numbered — it stands alone under its parent.
- Sub-heading numbers are hierarchical: `##` under `# 1` starts at `1.1`, `###` under `## 1.1` starts at `1.1.1`, etc.

### Inline color tags

Carried over from the existing RaisinDocs implementation:

- Inline: `<!--@fg:red-->text<!--/@fg-->`
- Block div: `<!--@div fg:red-->` / `<!--/@div-->`

## Open questions

### Setext headings

CommonMark supports underline-style headings:

```
Heading
=======
```

With "newline is a newline," the `=======` line is a separate line, not an underline. Drop setext headings entirely in RDMD? ATX style (`#`) is sufficient.

### Horizontal rules

CommonMark allows `---`, `***`, `___` with optional leading spaces. With spaces preserved, does `   ---` still produce a horizontal rule, or is it text with leading spaces?

### Lists inside blockquotes

`> - item` works, but how does TAB nesting interact with the `>` prefix?

```
> - Parent
> 	- Child (TAB after `> `)
```

Is the TAB after `>` structural (nesting), or does the `>` context change things?

### Block content inside list items

Can a list item contain code blocks, tables, or blockquotes? If so, how?

```
- Item with a code block:
	```
	code here
	```
- Item with a table:
	| A | B |
	|---|---|
	| 1 | 2 |
```

Does the TAB prefix apply to every line of the embedded block?

### Nested blockquotes

Are nested blockquotes supported?

```
> Outer quote
> > Inner quote
```

### Raw HTML

Is arbitrary HTML allowed in RDMD, or only the custom comment tags (`<!--@...-->`)? CommonMark allows inline and block HTML.

### Mid-line escaping

Line-start escaping is defined (`\1. Not a list`). Does mid-line backslash escaping follow CommonMark rules?

```
This is \*not italic\* and \`not code\`.
```

### Conversion

Is `.md` ↔ `.rdmd` conversion needed? Importing a `.md` file into RDMD would need to transform soft breaks into explicit line breaks, convert space-based indentation to TABs, etc.
