# Localization

Languages: English (`en`, default), Hebrew (`he`, right-to-left), Spanish (`es`).

English and Hebrew ship from the first build. Spanish is added before 1.0
(decision D-011).

## Rules from the first screen

- No user-visible text in XAML or C# code. All text lives in resources:
  `Strings.resx` (English), `Strings.he.resx`, `Strings.es.resx`.
- XAML binds text through one markup extension, so switching language updates
  the open window without a restart.
- Layouts never assume text width: no fixed-width buttons for text, and every
  page is tested in the longest language (usually Spanish).
- Dates, times and numbers use the selected culture. Logs, CLI output and
  `--json` output stay in English (invariant culture) so that scripts and
  support work in every language.

## Pseudo-localization (from the first build)

Spanish arrives late, but its longer text must not break layouts then. Debug
builds include a generated pseudo-language (`qps`) that wraps every English
string in brackets and lengthens it by about 40%, for example
`[Ĉĥéçķ ƒöŕ Ûþðåţéš ······]`. Every screen is checked in it. Text that is
cut off, overlaps or is missing its brackets (hard-coded) is a bug found
before any translation exists.

## Hebrew (right-to-left)

- The shell sets `FlowDirection="RightToLeft"` when the language is Hebrew.
  WPF mirrors the layout: the sidebar moves to the right.
- Icons with a direction (arrows, chevrons) are mirrored; logos, the mascot
  and Windows/Office product names are not.
- Product names, version numbers, file paths and KB numbers stay left-to-right
  inside Hebrew text.

## Language selection

1. Saved user choice (Settings page), if any.
2. Otherwise `WINTOOL_LANGUAGE` from the MSI, if not `auto`.
3. Otherwise the Windows display language, if supported.
4. Otherwise English.

## Cost

With these rules in place from the start, each new screen needs roughly
10–15% more work for resources and right-to-left checks. Every screen is
reviewed in four combinations: dark and light theme, left-to-right and
right-to-left.
