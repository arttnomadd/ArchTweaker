# ArchTweaker E2E — pytest fixtures (pywinauto + Windows UI Automation).
# Runs on Windows ONLY. ArchTweaker requires administrator rights, so run this from an ELEVATED terminal
# (or an elevated CI step). Tier-1 isolation: each run gets its own APPDATA/LOCALAPPDATA/TEMP.
import os, sys, subprocess, pytest

APP_PATH  = os.environ.get("APP_PATH", "")          # e.g. C:\path\to\ArchTweaker.exe
APP_TITLE = os.environ.get("APP_TITLE", "ArchTweaker")   # title bar starts with "ArchTweaker"
LAUNCH_TIMEOUT = int(os.environ.get("LAUNCH_TIMEOUT", "20"))
ARTIFACT_DIR = os.path.join(os.path.dirname(__file__), "artifacts")


@pytest.fixture(scope="function")
def app(request, tmp_path):
    if sys.platform != "win32":
        pytest.skip("Windows-only E2E")
    if not APP_PATH or not os.path.exists(APP_PATH):
        pytest.exit("Set APP_PATH to the built ArchTweaker.exe", returncode=1)

    from pywinauto import Application

    # The tests locate controls by their English names: start in English unless AT_LANG says otherwise.
    try:
        import winreg
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, r"Software\ArchTweaker") as k:
            winreg.SetValueEx(k, "Language", 0, winreg.REG_SZ, os.environ.get("AT_LANG", "en"))
    except OSError:
        pass

    env = os.environ.copy()
    env["APPDATA"]      = str(tmp_path / "Roaming");  os.makedirs(env["APPDATA"], exist_ok=True)
    env["LOCALAPPDATA"] = str(tmp_path / "Local");    os.makedirs(env["LOCALAPPDATA"], exist_ok=True)
    env["TEMP"] = env["TMP"] = str(tmp_path / "Temp"); os.makedirs(env["TEMP"], exist_ok=True)
    # 3.2: the first-run setup guide would cover the main window. Tests marked first_run get it, from a clean state.
    if request.node.get_closest_marker("first_run"):
        try:
            import winreg
            with winreg.CreateKey(winreg.HKEY_CURRENT_USER, r"Software\ArchTweaker") as k:
                try: winreg.DeleteValue(k, "SetupDone")
                except OSError: pass
        except OSError:
            pass
        env.pop("ARCHTWEAKER_NO_WIZARD", None)
    else:
        env["ARCHTWEAKER_NO_WIZARD"] = "1"

    # ArchTweaker self-elevates via its manifest; from an elevated shell this launches without a UAC prompt.
    proc = subprocess.Popen([APP_PATH], env=env)
    pw = Application(backend="uia").connect(process=proc.pid, timeout=LAUNCH_TIMEOUT)
    win = pw.window(title_re=r"ArchTweaker.*")
    try:
        win.wait("visible", timeout=LAUNCH_TIMEOUT)
    except Exception:
        proc.kill()
        pytest.exit("ArchTweaker window did not appear — is the terminal elevated (admin)?", returncode=1)
    yield win

    if getattr(getattr(request.node, "rep_call", None), "failed", False):
        os.makedirs(ARTIFACT_DIR, exist_ok=True)
        try: win.capture_as_image().save(os.path.join(ARTIFACT_DIR, "FAIL_%s.png" % request.node.name))
        except Exception: pass
    try: win.close(); proc.wait(timeout=5)
    except Exception: proc.kill()


@pytest.hookimpl(tryfirst=True, hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    setattr(item, "rep_%s" % outcome.get_result().when, outcome.get_result())
