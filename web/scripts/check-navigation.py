#!/usr/bin/env python3
"""
Reusable Playwright navigation smoothness & persistence benchmark for Shipit.
Measures shell DOM persistence, layout shifts (CLS), long tasks, time to
meaningful content, network request count, and skeleton/empty-state flashes
across a scripted SPA navigation sequence.

Usage (mock build):
  python web/scripts/check-navigation.py --port 4182 [--shots-dir <dir> --prefix before]

Usage (staging read-only):
  python web/scripts/check-navigation.py --base-url https://<staging-host> --username <user> --password <pass>
"""

import argparse
import json
import os
import sys
from playwright.sync_api import sync_playwright

INSTALL_OBSERVERS_JS = """() => {
  window.__navMetrics = {
    clsEntries: [],
    longTasks: [],
    skeletonSeen: false
  };

  if (typeof PerformanceObserver !== 'undefined') {
    try {
      const clsObs = new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          if (!entry.hadRecentInput) {
            window.__navMetrics.clsEntries.push({
              value: entry.value,
              startTime: entry.startTime
            });
          }
        }
      });
      clsObs.observe({ type: 'layout-shift', buffered: false });
    } catch {}

    try {
      const ltObs = new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          window.__navMetrics.longTasks.push({
            duration: entry.duration,
            startTime: entry.startTime
          });
        }
      });
      ltObs.observe({ type: 'longtask', buffered: false });
    } catch {}
  }

  const root = document.getElementById('app') || document.body;
  const mutObs = new MutationObserver(() => {
    const skel = root.querySelector('.skel-pulse, [aria-busy="true"]');
    if (skel) {
      window.__navMetrics.skeletonSeen = true;
    }
  });
  mutObs.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-busy', 'class'] });
}"""

TAG_SHELL_JS = """() => {
  const header = document.querySelector('header');
  const aside = document.querySelector('aside');
  const footer = document.querySelector('footer[data-testid="status-bar"]');
  if (header) header.dataset.shellInstance = 'initial-shell';
  if (aside) aside.dataset.shellInstance = 'initial-shell';
  if (footer) footer.dataset.shellInstance = 'initial-shell';
  return {
    headerTagged: Boolean(header),
    asideTagged: Boolean(aside),
    footerTagged: Boolean(footer)
  };
}"""

CHECK_SHELL_JS = """() => {
  const header = document.querySelector('header');
  const aside = document.querySelector('aside');
  const footer = document.querySelector('footer[data-testid="status-bar"]');
  return {
    headerSurvived: header?.dataset.shellInstance === 'initial-shell',
    asideSurvived: aside?.dataset.shellInstance === 'initial-shell',
    footerSurvived: footer?.dataset.shellInstance === 'initial-shell',
    clsMax: window.__navMetrics.clsEntries.reduce((m, e) => Math.max(m, e.value), 0),
    clsTotal: window.__navMetrics.clsEntries.reduce((s, e) => s + e.value, 0),
    longTasks: window.__navMetrics.longTasks.length,
    skeletonSeen: window.__navMetrics.skeletonSeen
  };
}"""

RESET_STEP_METRICS_JS = """() => {
  window.__navMetrics.clsEntries = [];
  window.__navMetrics.longTasks = [];
  window.__navMetrics.skeletonSeen = false;
}"""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", default="4182")
    parser.add_argument("--base-url", default="")
    parser.add_argument("--username", default="admin")
    parser.add_argument("--password", default="admin")
    parser.add_argument("--shots-dir", default="")
    parser.add_argument("--prefix", default="nav")
    parser.add_argument("--fail-on-regressions", action="store_true")
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/") if args.base_url else f"http://localhost:{args.port}"
    if args.shots_dir:
        os.makedirs(args.shots_dir, exist_ok=True)

    results = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1366, "height": 768})
        page = context.new_page()

        req_counter = {"count": 0}
        page.on("request", lambda req: req_counter.__setitem__("count", req_counter["count"] + 1))

        # Login & initial load on /
        page.goto(f"{base_url}/login", wait_until="networkidle")
        inputs = page.locator("input")
        if inputs.count() >= 2:
            inputs.nth(0).fill(args.username)
            inputs.nth(1).fill(args.password)
            page.locator("button[type='submit']").click()
            page.wait_for_load_state("networkidle")
            page.wait_for_timeout(400)
        page.goto(f"{base_url}/", wait_until="networkidle")
        page.wait_for_selector("tbody tr", timeout=10000)
        page.wait_for_timeout(200)

        page.evaluate(INSTALL_OBSERVERS_JS)
        tag_info = page.evaluate(TAG_SHELL_JS)

        steps = [
            ("Runs->Pipelines", "click-nav", 'aside nav a[href="/pipelines"]', "h1:has-text('Pipeline')"),
            ("Pipelines->Jobs", "click-nav", 'aside nav a[href="/jobs"]', "h1:has-text('Job')"),
            ("Jobs->Slaves", "click-nav", 'aside nav a[href="/slaves"]', "h1:has-text('Máy build')"),
            ("Slaves->Credentials", "click-nav", 'aside nav a[href="/credentials"]', "h1:has-text('Thông tin xác thực')"),
            ("Credentials->Allowlist", "click-nav", 'aside nav a[href="/settings/allowlist"]', "h1:has-text('Allowlist')"),
            ("Allowlist->Runs(revisit)", "click-nav", 'aside nav a[href="/"]', "h1:has-text('Lượt chạy')"),
            ("Runs->RunDetail", "click-row", 'tbody tr', "h1"),
            ("RunDetail->Back(revisit)", "go-back", "", "h1:has-text('Lượt chạy')"),
        ]

        for idx, (label, action, selector, wait_sel) in enumerate(steps, start=1):
            page.evaluate(RESET_STEP_METRICS_JS)
            req_counter["count"] = 0
            t0 = page.evaluate("() => performance.now()")

            if action == "click-nav":
                page.locator(selector).first.click()
            elif action == "click-row":
                page.locator(selector).first.click()
            elif action == "go-back":
                page.go_back()

            page.wait_for_timeout(50)
            if args.shots_dir:
                safe_name = label.replace("->", "_to_").replace("(", "_").replace(")", "")
                page.screenshot(path=f"{args.shots_dir}/{args.prefix}-{idx:02d}-{safe_name}-50ms.png")

            page.wait_for_selector(wait_sel, timeout=5000)
            page.wait_for_timeout(150)
            t1 = page.evaluate("() => performance.now()")

            metrics = page.evaluate(CHECK_SHELL_JS)
            results.append({
                "step": label,
                "shellSurvived": metrics["headerSurvived"] and metrics["asideSurvived"] and metrics["footerSurvived"],
                "headerSurvived": metrics["headerSurvived"],
                "asideSurvived": metrics["asideSurvived"],
                "footerSurvived": metrics["footerSurvived"],
                "clsMax": round(metrics["clsMax"], 4),
                "longTasks": metrics["longTasks"],
                "timeMs": round(t1 - t0, 1),
                "requests": req_counter["count"],
                "skeletonSeen": metrics["skeletonSeen"],
                "isRevisit": "revisit" in label
            })

        # Verify filter preservation on RunsView across rail navigation (/ -> /pipelines -> /)
        page.locator("aside a[href='/']").click()
        page.wait_for_url(f"{base_url}/**")
        page.wait_for_selector("tbody tr", timeout=10000)
        page.wait_for_timeout(150)
        unfiltered_rows = page.locator("tbody tr").count()

        filter_trigger = page.locator("[role='combobox']:has-text('Tất cả Pipeline / Job')").first
        filter_trigger.click()
        first_pipeline_opt = page.locator("[role='option']:has-text('Pipeline:')").first
        chosen_label = first_pipeline_opt.inner_text().strip()
        first_pipeline_opt.click()
        page.wait_for_timeout(300)
        filtered_rows = page.locator("tbody tr").count()
        filtered_url = page.url

        page.locator("aside a[href='/pipelines']").click()
        page.wait_for_url(f"{base_url}/pipelines**")
        page.wait_for_timeout(150)

        page.locator("aside a[href='/']").click()
        page.wait_for_url(f"{base_url}/**")
        page.wait_for_timeout(300)
        restored_rows = page.locator("tbody tr").count()
        restored_url = page.url
        filter_preserved = (
            "target=pipeline" in restored_url
            and restored_rows == filtered_rows
            and filtered_rows > 0
        )
        filter_check = {
            "chosenFilter": chosen_label,
            "unfilteredRows": unfiltered_rows,
            "filteredRows": filtered_rows,
            "restoredRows": restored_rows,
            "filteredUrl": filtered_url,
            "restoredUrl": restored_url,
            "filterPreserved": filter_preserved,
        }

        browser.close()

    print("TAG_INFO:", json.dumps(tag_info))
    print("NAV_RESULTS:", json.dumps(results, indent=2))
    print("FILTER_PRESERVATION:", json.dumps(filter_check))

    if args.fail_on_regressions:
        failures = []
        for r in results:
            if not r["shellSurvived"]:
                failures.append(f"Shell did not survive on {r['step']}")
            if r["clsMax"] > 0.01:
                failures.append(f"CLS > 0.01 ({r['clsMax']}) on {r['step']}")
            if r["isRevisit"] and r["skeletonSeen"]:
                failures.append(f"Skeleton shown on cached revisit {r['step']}")
        if not filter_check["filterPreserved"]:
            failures.append(f"Filter not preserved on RunsView revisit: {filter_check}")
        if failures:
            print("REGRESSIONS:", failures)
            sys.exit(1)

if __name__ == "__main__":
    main()
