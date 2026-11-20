import os
import sys
from playwright.sync_api import sync_playwright

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:4177"
SHOT_DIR = sys.argv[2] if len(sys.argv) > 2 else None
PREFIX = sys.argv[3] if len(sys.argv) > 3 else "check"

ROUTES = [
    ("/", "runs"),
    ("/runs/1", "run-detail"),
    ("/pipelines", "pipelines"),
    ("/jobs", "jobs"),
    ("/slaves", "slaves"),
    ("/credentials", "credentials"),
    ("/settings/allowlist?tab=deploy", "allowlist-deploy"),
    ("/settings/allowlist?tab=jobs", "allowlist-jobs"),
    ("/dev/components", "gallery"),
]

JS_CHECK_TABLES = """() => {
  const out = [];
  const tables = document.querySelectorAll('table');
  tables.forEach((tbl, idx) => {
    const wrap = tbl.parentElement;
    const tblRect = tbl.getBoundingClientRect();
    const wrapRect = wrap ? wrap.getBoundingClientRect() : tblRect;
    const ths = tbl.querySelectorAll('thead th');
    const lastTh = ths.length ? ths[ths.length - 1].getBoundingClientRect() : null;
    const firstTd = tbl.querySelector('tbody td');
    const lastTd = tbl.querySelector('tbody tr:first-child td:last-child');
    const lastTdRect = lastTd ? lastTd.getBoundingClientRect() : null;
    const lastTdOverflow = lastTd ? (lastTd.scrollWidth - lastTd.clientWidth) : 0;
    const lastTdButtons = lastTd ? Array.from(lastTd.querySelectorAll('button, a')) : [];
    const btnClipped = lastTdButtons.some(b => b.getBoundingClientRect().right > (lastTdRect ? lastTdRect.right + 1 : 99999));
    const overflowPx = wrap ? (wrap.scrollWidth - wrap.clientWidth) : 0;
    const clippedRight = lastTdRect ? (lastTdRect.right - wrapRect.right) : 0;
    out.push({
      idx,
      wrapScrollW: wrap ? wrap.scrollWidth : 0,
      wrapClientW: wrap ? wrap.clientWidth : 0,
      tblScrollW: tbl.scrollWidth,
      tblClientW: tbl.clientWidth,
      overflowPx,
      clippedRight: Math.round(clippedRight),
      lastTdOverflow: Math.round(lastTdOverflow),
      btnClipped,
      firstColW: firstTd ? Math.round(firstTd.getBoundingClientRect().width) : 0,
      lastColW: lastTh ? Math.round(lastTh.width) : 0
    });
  });
  return out;
}"""

JS_CHECK_LOGVIEWER_BADGE = """() => {
  const badges = Array.from(document.querySelectorAll('[role="status"]'));
  return badges
    .filter(b => (b.textContent || '').includes('Run đã kết thúc') && !b.classList.contains('sr-only'))
    .map(b => {
      const r = b.getBoundingClientRect();
      const parent = b.closest('.min-w-0') || b.closest('.overflow-x-auto');
      const pr = parent ? parent.getBoundingClientRect() : null;
      return {
        text: (b.textContent || '').trim(),
        scrollW: b.scrollWidth,
        clientW: b.clientWidth,
        rightClipped: pr ? Math.round(r.right - pr.right) : 0
      };
    });
}"""

def main():
    if SHOT_DIR:
        os.makedirs(SHOT_DIR, exist_ok=True)
    failures = []
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={"width": 1366, "height": 768})

        for vp_name, vp in [("1366x768", {"width": 1366, "height": 768}), ("1920x1080", {"width": 1920, "height": 1080})]:
            page.set_viewport_size(vp)
            for theme in ["dark", "light"]:
                page.goto(f"{BASE_URL}/", wait_until="networkidle")
                page.evaluate(
                    f"() => {{ if ('{theme}' === 'dark') document.documentElement.classList.add('dark'); else document.documentElement.classList.remove('dark'); }}"
                )
                for route, name in ROUTES:
                    page.goto(f"{BASE_URL}{route}", wait_until="networkidle")
                    page.wait_for_timeout(150)
                    tables = page.evaluate(JS_CHECK_TABLES)
                    for t in tables:
                        if t["overflowPx"] > 1 or t["clippedRight"] > 1 or t["lastTdOverflow"] > 1 or t["btnClipped"]:
                            msg = f"{vp_name} {theme} {name} table#{t['idx']}: scrollW={t['wrapScrollW']} > clientW={t['wrapClientW']} (overflow={t['overflowPx']}px, clippedRight={t['clippedRight']}px, lastTdOverflow={t['lastTdOverflow']}px, btnClipped={t['btnClipped']})"
                            print("OVERFLOW:", msg)
                            failures.append(msg)
                        elif vp_name == "1366x768" and theme == "dark":
                            print(f"OK {vp_name} {theme} {name} table#{t['idx']}: scrollW={t['wrapScrollW']} == clientW={t['wrapClientW']}, firstCol={t['firstColW']}px, lastCol={t['lastColW']}px")
                    if name == "run-detail" and vp_name == "1366x768" and theme == "dark":
                        lv = page.evaluate(JS_CHECK_LOGVIEWER_BADGE)
                        print("LogViewer status badge:", lv)
                        for b in lv:
                            if b["rightClipped"] > 1 or b["scrollW"] > b["clientW"] + 1:
                                failures.append(f"LogViewer badge clipped: {b}")
                    if SHOT_DIR and vp_name == "1366x768" and theme == "dark" and name in ("pipelines", "runs", "jobs", "run-detail"):
                        page.screenshot(path=os.path.join(SHOT_DIR, f"{PREFIX}-{name}.png"))
        browser.close()
    print(f"Total overflow/clipping issues: {len(failures)}")
    if failures and os.environ.get("STRICT") == "1":
        sys.exit(1)

if __name__ == "__main__":
    main()
