# Translating ArchTweaker

ArchTweaker shows its interface in English and every language that has a table in `lang/`.
A new language needs no code change: add a table, generate, build.

## Add a language

1. Copy `lang/ru.tsv` to `lang/<code>.tsv`, where `<code>` is the two-letter ISO 639-1 code (`de`, `fr`, `es`, `pl`...).
2. Change the second line to `# name: <the language's own name>`, for example `# name: Deutsch`. The language list shows this name.
3. Replace the text after the TAB on each line with your translation. Keep the English text before the TAB exactly as it is: it is the key.
4. Run `python3 tools/gen_l10n.py`. It writes `src/Generated/L10n<Code>.cs` and stops on errors.
5. Build (`build.bat` on Windows, `tools/build-win.sh` on Linux) and pick the language on the Settings page.

Windows starts ArchTweaker in your language when your Windows display language has a table.

## Rules for each line

- One entry per line: `English<TAB>Translation`. Exactly one TAB.
- Keep every placeholder: `{0}`, `{1}` and so on must appear in the translation too. The generator checks this.
- A plural form can choose the word by number: `{0|one|few|many}`. See the Russian table for examples.
- `\n` is a line break inside the text. Keep it where the English has one.
- Lines that start with `#` are comments.
- Leave a line out, or keep the English after the TAB, when you are not sure. ArchTweaker then shows the English text.

## Check your work

- `python3 tools/gen_l10n.py` must end without errors.
- `sh tools/run-tests.sh` runs the unit tests, including the checks for placeholders and duplicate keys.
- Open the app in your language and look at the Home, Tweaks and Settings pages.

## Send it

Open a pull request with `lang/<code>.tsv` and the generated `src/Generated/L10n<Code>.cs`. Name the language in the title.
