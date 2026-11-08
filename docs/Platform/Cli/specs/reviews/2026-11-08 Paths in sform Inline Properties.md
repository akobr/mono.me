# Paths in sform Inline Properties

## Overview

This review covers all of [Paths in sform Inline Properties](../2026-11-08%20Paths%20in%20sform%20Inline%20Properties.md) (Linear 424-213). The name of an `-x|--properties` value of `sform story config set` and `sform story template set` is now a JSON path in the syntax that the ConfigurationFormats library prints in its errors: `-x "db.port=5432"`, `-x "hosts[0]=a"`, `-x "Logging.LogLevel['Microsoft.AspNetCore']=Warning"`. The parser lives next to the printer, in the library, and `sform` writes each value into the built document at its path. A review of the finished branch (four reviewers, each finding checked by a skeptic) led to one fix commit and one documentation commit. It was not run against a live API.

| Commit | Content |
| --- | --- |
| `85cee373` | The spec, after three independent critiques of a first draft |
| `be075edb` | The parser: `JsonPaths.Parse`, `ParsePrefix`, `Of`, `JsonPathFormatException`, and its tests |
| `004546ca` | `sform`: `InlineProperties`, `JsonInputBuilder`, the help of `-x`, the tests |
| `c5a5a406` | The documentation |
| `c6c275a8` | The fixes of the review: replacing values Newtonsoft deems equal, the date strings of `-i`, the hint of an empty name, more tests |
| `06638be3` | The corrections of the review in the documentation |
| This commit | This review |

## What Was Done

### 1. The path syntax

As planned: `$` and steps that keep their `.` or `[`, or a first step without the dot and no `$`; plain names of ASCII letters, digits, `_` and `-`; `['name']` with `\'` and `\\`, and also `\n`, `\r`, `\t` and `\uXXXX` in any case, so a printed path parses back. That last part deviates from the issue's escape set, as the spec records; a literal backslash is always `\\`, and `['C:\temp']` reads a tab (pinned by a test). After `$`, a name character or another `$` is an error whose message says that such a name is written `['$name']`, so `-x '$schema=…'` cannot silently set `schema`.

### 2. The parser in the ConfigurationFormats library

- **`JsonPaths.ParsePrefix(text, out length)`** reads a path at the start of a text and stops before the first character that cannot continue it; `Parse(path)` also requires the end. Both throw `JsonPathFormatException : FormatException` with `Position`, whose messages say what was expected and never hold the text.
- **`JsonPaths.Of(segments)`** prints steps; a negative index throws `ArgumentOutOfRangeException`, and a segment with a name is a property whatever its index.
- **`IsPlain`** now shares `IsPlainStart` and `IsPlainPart` with the parser, so the printed and the parsed rule for a plain name are one rule.
- **Tests:** `JsonPathsParseTests`, 616 tests in the project with it: the forms, every escape, about fifty error positions, messages without the text, `ParsePrefix`, and the round trip over the names of `JsonPathsTests`, 2,000 seeded random step lists, and every error path of the embedded conformance vectors. A reviewer's separate fuzz run of 2,000,000 inputs found no other exception, no position outside the text and no round-trip failure.
- **Package:** the `<Description>` of the package and its line in `CLAUDE.md` mention the parser. No conformance vectors, as planned.

### 3. Inline properties in `sform`

- **`InlineProperties.Apply(document, inlineProperties)`** (`src/Platform/Cli/src/Commands/InlineProperties.cs`) follows the spec's steps: `$` alone, an index first and an empty name anywhere are refused before the walk; each step creates an object or an array for an absent property, a `null` or the next index, enters a fitting container, and refuses anything else with the path to it; an index past the end names the array and its length.
- **Writes go through `Put`**, which removes and inserts an item, and replaces a property in place with a new `JProperty`. The review found that Newtonsoft's indexers keep a value they deem equal, and they compare a date with only the wall-clock time of a date with an offset, so another instant was silently dropped (the old merge dropped it too, for top-level names). An entered container is never set again, as setting a token that has a parent puts a copy in its place.
- **Not in the spec:** `JsonInputBuilder` reads the `-i` file with `DateParseHandling.None`, as the SDK reads. Before, a date-like string of the file became a date converted to the local time zone, so its text changed when it was sent, and an error called it "a date". Now it stays the string of the file.
- **Values** go through `PropertyValueParser` as before; for a plain name the document is the one of the old merge (a test compares them).

### 4. Errors

The messages of the spec's table, with two changes from the review:

- **An empty name** names the shells: "A shell may have expanded a $name of the argument: in bash and zsh write \$ inside the double quotes, in PowerShell use single quotes and double the quotes of ['…']." The spec's "write \$ there, or use single quotes" was wrong for PowerShell, and single quotes in bash end at the quotes of the path.
- **The hint to quote the argument** is added when `[` is followed by anything but a digit or a quote, as a shell leaves an unquoted `a['b.c']=1`.

`JsonInputBuilder` writes its messages with `WriteErrorLine`, one plain line on standard error, also the one of a missing `-i` file. A test checks that a name with U+2028 and `##[x]` stays one line with the hash escaped. No message holds a value, also for an argument without `=` (`-x "$API_KEY"`): the "no '='" message names the character, not the path.

### 5. Help and documentation

- **The help of `-x`** is the spec's, with quoted examples; checked in the built `sform story config set --help`.
- **`docs/42for.net/cli/sform.md`**, "Write a configuration": the synopsis, examples, the table of steps, how values are written, errors, what the API's merge does with the document, shells and the change, and the warning about an older `sform`. **Deviation from the spec:** the spec said to drop the leading `$` of a printed path in bash and zsh. Only `$[` is arithmetic expansion there, and dropping the `$` of `$.db.port` leaves `.db.port`, which the parser refuses; the section says to drop it for a path that starts with `$[`.
- **`docs/Platform/Storyteller/configuration-formats.md`**: `JsonPathFormatException` in the list of types, two rows in the table of the library, and the bullet "Paths are also input". The review corrected one sentence: a raw control character or `#` is accepted, not every character the printer escapes (a raw `'` closes the name and a raw `\` starts an escape).
- **`docs/Platform/Storyteller/templating.md`**: the synopsis, the row of `set` and an example.
- **`PlainOutputExtensions.cs`**: the comment of `WriteNumberedText` names the escape of a hash again; it had lost it.
- **Release notes:** the subject of `004546ca` names the change of meaning. No message of the branch holds the text that `mrepo` reads as a breaking change.

### 6. Tests

- **`InlinePropertiesTests`**, 76 tests, through `JsonInputBuilder.BuildAsync` with a `MockFileSystem` and the standard error of the console: every row of the spec's table, names that were literal before and now fail, items after the first (replaced and entered), an array deep in a path, the place of a replaced property, the kinds of crossed values (number, time span, string, date), date strings of the file, dates that Newtonsoft deems equal, and `ConfigSetCommand` sending the nested document or nothing.
- **Mutations the review used** to find gaps in the first tests (a wrong index on an existing item, an empty-name check of the last step only, a weaker check after `$`, `IsDigit` for `IsAsciiDigit` in the digits of an index, wider hex digits, the kinds in messages) are each caught by a test now. One survives: `IsDigit` in the check of the first character of an index, whose input still fails at the same position, with another message.
- **Runs:** ConfigurationFormats 616 passed; the whole CLI project 1179 passed; the configuration provider, which also uses `JsonPaths`, 639 passed and 2 skipped (Unix only).

### 7. Out of scope

As planned. Checks against the stored content stay out: the documentation explains that an index addresses the built array, that the API unions arrays and replaces a value of another kind.

### 8. Delivery

The built `sform` printed the new help, and two wrong `-x` values ended with exit code `2` and one line on standard error before any request. Not run against a live API. The skeptics of the review rejected three findings. One is worth a note: `JsonPaths.Of` builds its text step by step, which is quadratic in the number of steps; its only caller is the error messages of `sform`, whose input the command line limits (about a second for a 128 KiB argument of `[0]` steps on Linux), so it was left as it is.
