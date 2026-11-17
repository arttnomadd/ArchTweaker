# ArchTweaker E2E (Windows desktop)

End-to-end UI smoke tests using **pywinauto** over Windows UI Automation. They launch the real
`ArchTweaker.exe`, click through every sidebar page, and assert the app stays alive and responsive.

## Why these exist
The headless unit tests (`../Tests/`) cover pure logic. These cover the thing logic tests can't: that the
WinForms UI actually builds and every page constructs on a real Windows box. Together they're the safety net
the code review flagged as missing.

## Requirements
- **Windows 10/11** (these cannot run on Linux/macOS).
- Python 3.8+.
- An **elevated** terminal — ArchTweaker requests administrator rights via its manifest, so pytest must be
  started from an admin shell (or an elevated CI step), otherwise the window never appears.

```bat
pip install -r requirements.txt
```

## Run
```bat
set APP_PATH=C:\path\to\ArchTweaker.exe
pytest -m smoke
```
Artifacts (HTML report, failure screenshots) land in `e2e\artifacts\`.

## What's covered
- `test_window_opens` — the main window launches and is titled.
- `test_each_page_opens_without_crashing` — parametrized over all 22 sidebar pages; each navigates and the
  header label updates, with the window still responsive.
- `test_full_navigation_sweep_leaves_app_alive` — visits every page in one session; catches state-leak crashes.

Locators use `AccessibleName` (WinForms → UIA AutomationId): nav items expose their page name and the header
exposes `pageTitle`. Add more `AccessibleName`s to controls as you add deeper interaction tests.

## CI (manual / self-hosted)
`windows-latest` runners are not elevated by default, so the admin-gated launch may not work on hosted CI
without a self-hosted elevated runner. A starting point:

```yaml
name: Desktop E2E
on: workflow_dispatch        # manual — needs an elevated/self-hosted Windows runner
jobs:
  e2e:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with: { python-version: "3.11" }
      - run: pip install -r e2e/requirements.txt
      - name: Build
        run: |   # build ArchTweaker.exe with your Windows toolchain, or download the release artifact
          echo build step here
      - name: E2E
        env:
          APP_PATH: ${{ github.workspace }}\ArchTweaker.exe
        run: pytest e2e -m smoke
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: e2e-artifacts, path: e2e/artifacts/ }
```
