# ArchTweaker E2E smoke tests. Navigate every page and confirm the app stays alive and responsive.
# Locators use the UIA AutomationId, which WinForms takes from Control.Name: nav items set Name to their page name,
# and the header label exposes AutomationId "pageTitle". The tests run in Pro mode (conftest), except those marked "lite".
import time
import pytest

# Pages reachable from the sidebar (sections auto-expand on click), in sidebar order: Home, the tweak
# categories (Catalog.Categories), then the pages registered with AddNav in src/Ui/MainForm.cs.
# 3.35.1: a unit test (UiTests.E2ePageList_MatchesTheSidebar) fails when this list and the sidebar differ.
PAGES = [
    "Home", "Privacy", "Interface", "Explorer & Context Menu", "System", "Responsiveness", "Gaming", "Power Plan",
    "Input & Latency", "GPU & Devices", "Internet & Network", "Hardening", "Tasks & Services", "Command tweaks",
    "Danger zone", "PC advisor", "System scan", "Hardware audit", "System info", "Boot times", "Latency",
    "Benchmark", "Measure", "A/B test", "Live overlay", "Mouse polling", "Tuning assistant", "Interrupt tuner",
    "Power tuner", "Tune everything", "What changed", "Results", "Change history", "Cleanup", "Deep clean",
    "Disk space", "Uninstaller", "Apps (UWP)", "Startup", "Services", "Power settings", "Context menu",
    "Hosts blocker", "Drivers", "Bake into ISO",
]


# Pro-only pages (Mode.ProPages in src/Modes/LiteProMode.cs; a unit test keeps the two in step). Lite mode hides them.
PRO_PAGES = {
    "Tuning assistant", "Interrupt tuner", "Power tuner", "Tune everything", "A/B test", "Deep clean", "Services", "Apps (UWP)",
    "Power settings", "Hosts blocker", "Drivers", "Bake into ISO",
}


def _hidden_pro_page(win, name):
    """3.35.1: True only for a Pro-only page that this (Lite) session hides. Any other missing page fails the test."""
    return name in PRO_PAGES and not win.child_window(auto_id=name, control_type="TabItem").exists(timeout=3)


def _title(win, expected, timeout=15):
    """The header label's text, once it reads `expected` or the time is up."""
    title = win.child_window(auto_id="pageTitle")
    end = time.time() + timeout
    shown = title.window_text()
    while shown != expected and time.time() < end:
        time.sleep(0.25)
        shown = title.window_text()
    return shown


def _nav(win, name, timeout=60):
    """Open a sidebar page and return the header text.
    3.38.4: types the page name into the sidebar's "Find a page" box first. The filter shows matching items at the
    top of the sidebar, so the item is on screen even when its section is closed or scrolled away, and a real click
    lands on it. Waits while the sidebar is off (ArchTweaker busy), retries the click once, then clears the filter."""
    box = win.child_window(auto_id="findPage", control_type="Edit")
    box.wait("exists enabled", timeout=timeout)
    item = win.child_window(auto_id=name, control_type="TabItem")
    shown = None
    try:
        box.set_edit_text(name)
        item.wait("exists visible enabled", timeout=timeout)
        for _ in range(2):
            item.click_input()
            shown = _title(win, name)
            if shown == name:
                break
    finally:
        try:
            box.set_edit_text("")
        except Exception:
            pass
    return shown


def _dialog(win, title, timeout=30):
    """A dialog ArchTweaker opens. WinForms owned dialogs sit under the main window in UI Automation,
    not under the desktop, so look in both places."""
    from pywinauto import Desktop
    end = time.time() + timeout
    while time.time() < end:
        for spec in (win.child_window(title=title, control_type="Window"), Desktop(backend="uia").window(title=title)):
            try:
                if spec.exists(timeout=0.5):
                    return spec
            except Exception:
                pass
        time.sleep(0.5)
    raise AssertionError("dialog '%s' did not open" % title)


@pytest.mark.smoke
def test_window_opens(app):
    # Arrange/Act: fixture launched it. Assert: the window is visible and titled.
    assert app.is_visible()
    assert "ArchTweaker" in app.window_text()


@pytest.mark.smoke
@pytest.mark.parametrize("page", PAGES)
def test_each_page_opens_without_crashing(app, page):
    if _hidden_pro_page(app, page):
        pytest.skip("'%s' is a Pro-only page; this session is in Lite mode" % page)
    # Act
    shown = _nav(app, page)
    # Assert: the header updated to this page, and the window is still responsive.
    assert shown == page, "header should read '%s', got '%s'" % (page, shown)
    assert app.is_visible(), "window should remain open after navigating to %s" % page


@pytest.mark.smoke
@pytest.mark.timeout(900)   # 3.38.4: ~45 pages at a few seconds each; the 120 s default stopped the whole run
def test_full_navigation_sweep_leaves_app_alive(app):
    # Act: visit every page in sequence.
    for page in PAGES:
        if not _hidden_pro_page(app, page):
            _nav(app, page)
    # Assert: no crash across the whole sweep.
    assert app.is_visible()
    assert "ArchTweaker" in app.window_text()


@pytest.mark.smoke
def test_controls_expose_names_to_screen_readers(app):
    # Assert: the 2.5 accessibility pass named the top-bar inputs and the View menu button (3.1: theme moved into View).
    for name in ("Search all tweaks", "Preset", "View options", "Pages"):
        # found_index: a combo box and its inner list can share the name
        assert app.child_window(title=name, found_index=0).exists(timeout=5), "missing accessible name: %s" % name
    # the tweak filter tabs are on tweak pages only
    _nav(app, "Privacy")
    assert app.child_window(title="Show tweaks").exists(timeout=10), "missing accessible name: Show tweaks"


@pytest.mark.smoke
def test_sidebar_arrow_keys_move_focus(app):
    # Arrange: focus the first sidebar entry.
    home = app.child_window(title="Home", control_type="TabItem")
    home.set_focus()
    # Act
    home.type_keys("{DOWN}")
    # Assert: focus left "Home" and stayed inside the sidebar.
    assert not home.has_keyboard_focus(), "Down should move focus to the next sidebar entry"


@pytest.mark.smoke
@pytest.mark.parametrize("page", ["A/B test", "Latency", "What changed"])
def test_more_pages_open(app, page):
    # Act: open a page added in 2.7 from the sidebar. Assert: the page title follows.
    assert _nav(app, page) == page


@pytest.mark.smoke
def test_command_palette_opens_and_navigates(app):
    # Act: Ctrl+K, type a page name, Enter.
    # 3.38.4: a real click on the page title first, so the keys reach ArchTweaker and not Windows search
    app.child_window(auto_id="pageTitle").click_input()
    app.type_keys("^k")
    import time; time.sleep(1)
    from pywinauto import keyboard
    keyboard.send_keys("Disk{SPACE}space{ENTER}", pause=0.05)
    # Assert
    assert _title(app, "Disk space") == "Disk space"


@pytest.mark.smoke
def test_settings_page_has_language_and_scheme(app):
    # 3.38.4: language and colour scheme live on Settings (Tools & backup no longer has them)
    assert _nav(app, "Settings") == "Settings"
    assert app.child_window(title="Language", found_index=0).exists(timeout=10)
    assert app.child_window(title="Colour scheme", found_index=0).exists(timeout=10)


# ---- opt-in: changes the PC. CI sets AT_E2E_APPLY=1 on its throwaway VM. ----
import os
APPLY = os.environ.get("AT_E2E_APPLY") == "1"


@pytest.mark.vm
@pytest.mark.timeout(900)
@pytest.mark.skipif(not APPLY, reason="changes the PC: set AT_E2E_APPLY=1 on a throwaway VM")
def test_apply_safe_preset_then_undo_everything(app):
    from pywinauto import Desktop
    # Arrange: pick the Recommended preset and select its tweaks.
    preset = app.child_window(title="Preset", control_type="ComboBox")
    preset.select("Recommended")
    app.child_window(title="Select preset", control_type="Button").click_input()
    # Act 1: apply through the review dialog (no restore point on CI).
    rp = app.child_window(title="Create restore point before first apply", control_type="CheckBox")
    if rp.get_toggle_state() == 1:
        rp.click_input()
    app.child_window(auto_id="applyChanges", control_type="Button").click_input()
    review = _dialog(app, "Review changes")
    review.child_window(title_re=r"Apply \d+ changes?", control_type="Button").click_input()
    app.child_window(title="Undo last apply", control_type="Button").wait("enabled", timeout=300)
    # Act 2: undo everything.
    app.child_window(title="Undo everything", control_type="Button").click_input()
    dlg = Desktop(backend="uia").window(title="ArchTweaker", control_type="Window", found_index=0)
    dlg.child_window(title="Undo everything", control_type="Button").click_input()
    # Assert: the app is still alive and nothing is pending.
    app.child_window(auto_id="applyChanges", control_type="Button").wait("visible", timeout=300)
    assert app.is_visible()


@pytest.mark.ru
@pytest.mark.skipif(os.environ.get("AT_LANG") != "ru", reason="run with AT_LANG=ru")
def test_russian_interface(app):
    # The header shows the translated page name and the Tools page is reachable by its Russian name.
    title = app.child_window(auto_id="pageTitle")
    title.wait("visible", timeout=10)
    assert title.window_text() == "Главная"
    item = app.child_window(auto_id="Tools & backup", control_type="TabItem")   # the ID is the English page name
    item.wait("exists enabled", timeout=60)
    item.iface_legacy_iaccessible.DoDefaultAction()
    assert _title(app, "Инструменты и бэкап") == "Инструменты и бэкап"


# ---- 3.2 ----
def _hkcu(name):
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\ArchTweaker") as k:
            return winreg.QueryValueEx(k, name)[0]
    except OSError:
        return None


@pytest.mark.smoke
@pytest.mark.first_run
def test_first_run_guide_can_be_skipped(app):
    # Arrange: SetupDone was cleared, so the guide opens over the main window.
    guide = _dialog(app, "Set up ArchTweaker", timeout=60)
    # Act
    guide.child_window(title="Skip setup", control_type="Button").click_input()
    # Assert: the guide closed, the choice is remembered and nothing was ticked.
    guide.wait_not("visible", timeout=10)
    assert _hkcu("SetupDone") == 1
    assert app.child_window(auto_id="applyChanges", control_type="Button").exists(timeout=5)


@pytest.mark.smoke
def test_tweak_page_opens_with_summary(app):
    # Act
    assert _nav(app, "Gaming") == "Gaming"
    # Assert: no tweak selected, so the details panel shows the page summary.
    assert app.child_window(title="PAGE SUMMARY").exists(timeout=10)


@pytest.mark.smoke
def test_idle_buttons_are_hidden(app):
    # Assert: nothing pending and nothing applied in this session: Discard has nothing to do.
    assert not app.child_window(title="Discard changes", control_type="Button").exists(timeout=2)
    # 3.33: with nothing pending the button reads "Nothing to apply"; its ID stays "applyChanges"
    assert app.child_window(auto_id="applyChanges", control_type="Button").exists(timeout=5)


@pytest.mark.smoke
def test_arrow_keys_walk_the_tweak_list(app):
    # Arrange: focus the first tweak row on a page.
    assert _nav(app, "Privacy") == "Privacy"
    rows = app.descendants(control_type="CheckBox")
    rows = [r for r in rows if r.window_text() and r.window_text() != "Create restore point before first apply"]
    assert len(rows) >= 2, "expected tweak rows on Privacy"
    rows[0].set_focus()
    # Act
    rows[0].type_keys("{DOWN}")
    # Assert: focus moved to another row.
    assert not rows[0].has_keyboard_focus(), "Down should move to the next tweak"


@pytest.mark.smoke
def test_view_menu_has_its_options(app):
    from pywinauto import Desktop
    # Act: open the View menu.
    app.child_window(title="View options", control_type="Button").click_input()
    # 3.38.4: the drop-down is a top-level window of ArchTweaker's own process (found_index=0 found other menus)
    pid = app.element_info.process_id
    end = time.time() + 10
    names = set()
    while time.time() < end:
        names = set()
        for w in Desktop(backend="uia").windows(process=pid):
            for d in w.descendants(control_type="MenuItem"):
                names.add(d.window_text())
        for d in app.descendants(control_type="MenuItem"):
            names.add(d.window_text())
        if "Slim sidebar" in names:
            break
        time.sleep(0.5)
    # Assert (3.37: colour schemes, accents, text size and sorting are submenus; these are top-level items)
    for item in ("Switch dark / light", "Follow Windows theme", "Compact rows", "Slim sidebar"):
        assert item in names, "missing menu item: %s (found: %s)" % (item, sorted(names))
    from pywinauto import keyboard
    keyboard.send_keys("{ESC}")


@pytest.mark.smoke
def test_log_has_filter_and_support_copy(app):
    # Arrange: open the log if it is closed.
    show = app.child_window(title="Show log", control_type="Button")
    if show.exists(timeout=2):
        show.click_input()
    # Assert
    assert app.child_window(title="Show in log", control_type="ComboBox").exists(timeout=5)
    assert app.child_window(title="Copy for support", control_type="Button").exists(timeout=5)


@pytest.mark.smoke
def test_danger_zone_is_locked_and_asks_before_unlocking(app):
    # 3.4: the Danger zone opens locked; its unlock button opens a confirmation that "Keep locked" closes again.
    assert _nav(app, "Danger zone") == "Danger zone"
    unlock = app.child_window(title_re="Unlock for this session.*", control_type="Button")
    assert unlock.exists(timeout=10), "Danger zone banner shows the unlock button"
    unlock.click_input()
    gate = _dialog(app, "Unlock the Danger zone", timeout=10)
    gate.child_window(title="Keep locked", control_type="Button").click_input()
    assert app.child_window(title_re="Unlock for this session.*", control_type="Button").exists(timeout=5), "still locked"


@pytest.mark.smoke
@pytest.mark.lite
def test_lite_hides_pro_pages(app):
    # 3.38.4: Lite mode (the default for new users) shows the everyday pages only.
    assert _nav(app, "Cleanup") == "Cleanup"
    for page in ("Drivers", "Bake into ISO", "Danger zone", "Command tweaks"):
        assert not app.child_window(auto_id=page, control_type="TabItem").exists(timeout=2), page + " is hidden in Lite"


@pytest.mark.smoke
@pytest.mark.parametrize("button", ["Ghost devices…", "Network test (ping + jitter)"])
def test_new_tools_are_reachable(app, button):
    # 3.5: ghost devices (Drivers page) and the network test (Measure page) are one click away.
    page = "Drivers" if button.startswith("Ghost") else "Measure"
    assert _nav(app, page) == page
    assert app.child_window(title=button, control_type="Button").exists(timeout=10)
