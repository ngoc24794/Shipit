#!/usr/bin/env python3
"""
Reusable Playwright overlap & clipping sweep for Shipit web UI.
Checks sibling interactive elements/labels inside toolbars, table headers,
card headers, rail, and status bar for >2px bounding-box intersections or
unintended ellipsis clipping across routes, viewports, split-pane widths, and themes.

Usage:
  python web/scripts/check-overlaps.py --port 4181 [--shots-dir <dir> --prefix before]
"""

import argparse
import os
import sys
from playwright.sync_api import sync_playwright

CHECK_OVERLAPS_JS = """() => {
  const issues = [];

  function visibleRect(el) {
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || parseFloat(style.opacity) === 0) {
      return null;
    }
    const r = el.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return null;
    return r;
  }

  // 1. Check sibling overlaps inside toolbars, headers, rail, status bar, table headers
  const containers = document.querySelectorAll(
    'header, footer[data-testid="status-bar"], aside nav, thead tr, [data-testid="log-viewer-toolbar"], [data-testid="data-toolbar"]'
  );
  containers.forEach((container, cIdx) => {
    const children = Array.from(container.children).filter((el) => visibleRect(el) !== null);
    for (let i = 0; i < children.length; i++) {
      const aEl = children[i];
      const a = visibleRect(aEl);
      if (!a) continue;
      for (let j = i + 1; j < children.length; j++) {
        const bEl = children[j];
        const b = visibleRect(bEl);
        if (!b) continue;
        const overlapX = Math.min(a.right, b.right) - Math.max(a.left, b.left);
        const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
        if (overlapX > 2 && overlapY > 2) {
          issues.push({
            type: 'sibling-overlap',
            container: container.tagName.toLowerCase() + (container.dataset.testid ? `[${container.dataset.testid}]` : ''),
            a: (aEl.innerText || aEl.getAttribute('aria-label') || aEl.tagName).trim().slice(0, 40),
            b: (bEl.innerText || bEl.getAttribute('aria-label') || bEl.tagName).trim().slice(0, 40),
            overlapX: Math.round(overlapX),
            overlapY: Math.round(overlapY)
          });
        }
      }
    }
  });

  // 2. Check interactive controls inside toolbars/status-bar for overlap with each other
  document.querySelectorAll('[data-testid="log-viewer-toolbar"], header, footer[data-testid="status-bar"]').forEach((bar) => {
    const controls = Array.from(bar.querySelectorAll('button, input, [role="combobox"], [role="status"]'))
      .filter((el) => visibleRect(el) !== null);
    for (let i = 0; i < controls.length; i++) {
      const aEl = controls[i];
      const a = visibleRect(aEl);
      for (let j = i + 1; j < controls.length; j++) {
        const bEl = controls[j];
        if (aEl.contains(bEl) || bEl.contains(aEl)) continue;
        const b = visibleRect(bEl);
        const overlapX = Math.min(a.right, b.right) - Math.max(a.left, b.left);
        const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
        if (overlapX > 2 && overlapY > 2) {
          issues.push({
            type: 'control-overlap',
            bar: bar.dataset.testid || bar.tagName.toLowerCase(),
            a: (aEl.innerText || aEl.getAttribute('aria-label') || aEl.tagName).trim().slice(0, 40),
            b: (bEl.innerText || bEl.getAttribute('aria-label') || bEl.tagName).trim().slice(0, 40),
            overlapX: Math.round(overlapX),
            overlapY: Math.round(overlapY)
          });
        }
      }
    }
  });

  // 3. Check non-data buttons / status badges / th labels for unintended clipping
  document.querySelectorAll('thead th, [data-testid="log-viewer-toolbar"] button, [data-testid="log-viewer-toolbar"] [role="status"]').forEach((el) => {
    const r = visibleRect(el);
    if (!r) return;
    if (el.scrollWidth > el.clientWidth + 2) {
      issues.push({
        type: 'clipped-control',
        tag: el.tagName.toLowerCase(),
        text: (el.innerText || el.getAttribute('aria-label') || '').trim().slice(0, 40),
        scrollWidth: el.scrollWidth,
        clientWidth: el.clientWidth
      });
    }
  });

  return issues;
}"""

ROUTES = [
    ("runs", "/"),
    ("run-detail", "/runs/5"),
    ("pipelines", "/pipelines"),
    ("pipelines-new", "/pipelines/new"),
    ("jobs", "/jobs"),
    ("jobs-new", "/jobs/new"),
    ("slaves", "/slaves"),
    ("credentials", "/credentials"),
    ("allowlist-deploy", "/settings/allowlist"),
    ("allowlist-job", "/settings/allowlist?tab=job"),
    ("gallery", "/dev/components"),
]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", default="4181")
    parser.add_argument("--shots-dir", default="")
    parser.add_argument("--prefix", default="")
    parser.add_argument("--fail-on-issues", action="store_true")
    args = parser.parse_args()

    base_url = f"http://localhost:{args.port}"
    if args.shots_dir:
        os.makedirs(args.shots_dir, exist_ok=True)

    findings = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1366, "height": 768})
        page = context.new_page()

        # Login
        page.goto(f"{base_url}/login", wait_until="networkidle")
        inputs = page.locator("input")
        if inputs.count() >= 2:
            inputs.nth(0).fill("admin")
            inputs.nth(1).fill("admin")
            page.locator("button[type='submit']").click()
            page.wait_for_load_state("networkidle")
            page.wait_for_timeout(300)

        for theme in ["dark", "light"]:
            if theme == "light":
                cmd = page.locator('[data-testid="command-bar-input"]')
                if cmd.count() > 0:
                    cmd.click()
                    cmd.fill("theme light")
                    cmd.press("Enter")
                    page.wait_for_timeout(150)

            for vp in [(1366, 768), (1920, 1080)]:
                page.set_viewport_size({"width": vp[0], "height": vp[1]})
                for name, path in ROUTES:
                    page.goto(f"{base_url}{path}", wait_until="networkidle")
                    page.wait_for_timeout(250)

                    issues = page.evaluate(CHECK_OVERLAPS_JS)
                    if issues:
                        findings.append((f"{name}@{vp[0]}x{vp[1]}({theme})", issues))

                    if name == "run-detail":
                        if args.shots_dir and vp == (1366, 768):
                            page.screenshot(path=f"{args.shots_dir}/{args.prefix}-run5-1366-{theme}-default.png")
                        divider = page.locator('[data-testid="split-pane-divider"]')
                        if divider.count() > 0:
                            divider.focus()
                            divider.press("Home")
                            page.wait_for_timeout(150)
                            issues_min = page.evaluate(CHECK_OVERLAPS_JS)
                            if issues_min:
                                findings.append((f"{name}-min@{vp[0]}x{vp[1]}({theme})", issues_min))

                            divider.press("End")
                            page.wait_for_timeout(150)
                            if args.shots_dir and vp == (1366, 768):
                                page.screenshot(path=f"{args.shots_dir}/{args.prefix}-run5-1366-{theme}-max-left.png")
                            issues_max = page.evaluate(CHECK_OVERLAPS_JS)
                            if issues_max:
                                findings.append((f"{name}-max@{vp[0]}x{vp[1]}({theme})", issues_max))

        browser.close()

    print(f"OVERLAP_FINDINGS_COUNT: {len(findings)}")
    for loc, iss in findings:
        print("FINDING:", loc, iss)

    if args.fail_on_issues and findings:
        sys.exit(1)

if __name__ == "__main__":
    main()
