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

### Tabs vs spaces

- **TAB** = structural (nesting)
- **Spaces** = visual (preserved as-is, no structural meaning)

Tab display width: default 4, configurable (global setting).

### No indented code blocks

The CommonMark rule "4 spaces of indentation = code block" is removed. Since leading spaces are preserved and meaningful, this rule would conflict. Use fenced code blocks (`` ``` ``) exclusively.

## Lists

### List continuation

Lines following a list item belong to that item until the next list marker or a blank line (new paragraph).

Leading spaces on continuation lines are just spaces — they carry no structural meaning (no nesting detection, no content-column alignment). Indentation within a list item is done with TAB.

```
1. This is a long list item
   this is just a line in the first item with leading spaces.
	This line is indented (starts with TAB).
2. Second item - continuation after 1.
```

A blank line ends the list. To continue writing after a list, start a new paragraph.

```
1. Item one
2. Item two

This is a new paragraph, not part of the list.
```

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
# 1. Introduction
## 1.1 Background
## 1.2 Scope
# 2. Architecture
## 2.1 Overview
## This heading is not numbered
# 3. Implementation
```

The space after `#` is optional:

```
#This is a Heading1
# This is also a Heading1
```

Rules:
- A heading with a number prefix (e.g. `# 1.`) is numbered. The editor renumbers on insert/delete/reorder.
- A heading without a number prefix is not numbered — it stands alone under its parent.
- Sub-heading numbers are hierarchical: `##` under `# 1.` starts at `1.1`, `###` under `## 1.1` starts at `1.1.1`, etc.

### Inline color tags

Carried over from the existing RaisinDocs implementation:

- Inline: `<!--@fg:red-->text<!--/@fg-->`
- Block div: `<!--@div fg:red-->` / `<!--/@div-->`
