#!/usr/bin/env python3
"""
scripts/validate-manifests.py
Validates Kubernetes manifests and Docker ignore rules for Shipit packaging.
"""

import sys
from pathlib import Path
import re
import yaml

REPO_ROOT = Path(__file__).resolve().parent.parent
K8S_DIR = REPO_ROOT / "deploy" / "k8s"
DOCKERIGNORE_FILE = REPO_ROOT / ".dockerignore"
SECRET_EXAMPLE_FILE = K8S_DIR / "secret.example.yaml"


def fail(msg: str):
    print(f"[FAIL] {msg}", file=sys.stderr)
    sys.exit(1)


def info(msg: str):
    print(f"[OK] {msg}")


def main():
    print(f"Validating Kubernetes manifests under {K8S_DIR} ...")

    if not K8S_DIR.is_dir():
        fail(f"Directory {K8S_DIR} does not exist.")

    # 1. Parse all YAML under deploy/k8s
    yaml_files = sorted(list(K8S_DIR.rglob("*.yaml")) + list(K8S_DIR.rglob("*.yml")))
    if not yaml_files:
        fail("No YAML files found under deploy/k8s.")

    docs = []
    for yf in yaml_files:
        try:
            with open(yf, "r", encoding="utf-8") as f:
                loaded = list(yaml.safe_load_all(f))
                for doc in loaded:
                    if doc is not None:
                        doc["_source_file"] = str(yf.relative_to(REPO_ROOT))
                        docs.append(doc)
        except Exception as e:
            fail(f"Error parsing YAML file {yf}: {e}")

    info(f"Parsed {len(docs)} documents across {len(yaml_files)} YAML files.")

    # Check 6: No PersistentVolumeClaim or Ingress anywhere
    for doc in docs:
        kind = doc.get("kind")
        if kind in ("PersistentVolumeClaim", "Ingress"):
            fail(f"Disallowed kind '{kind}' found in {doc.get('_source_file')}.")
    info("No PersistentVolumeClaim or Ingress found.")

    # Check 2, 3, 4: RBAC Role & ClusterRole rules
    roles = [d for d in docs if d.get("kind") in ("Role", "ClusterRole")]
    if not roles:
        fail("No Role or ClusterRole found in manifests.")

    for role in roles:
        role_name = role.get("metadata", {}).get("name", "<unknown>")
        ns = role.get("metadata", {}).get("namespace", "<cluster>")
        rules = role.get("rules", [])
        if not rules:
            fail(f"Role '{role_name}' in namespace '{ns}' has no rules.")

        for rule in rules:
            # Check 2: No wildcard '*' in any rule field
            for key, val in rule.items():
                if isinstance(val, list):
                    for item in val:
                        if "*" in str(item):
                            fail(f"Wildcard '*' found in Role '{role_name}' field '{key}': {item}")
                elif isinstance(val, str) and "*" in val:
                    fail(f"Wildcard '*' found in Role '{role_name}' field '{key}': {val}")

            verbs = [v.lower() for v in rule.get("verbs", [])]
            resources = [r.lower() for r in rule.get("resources", [])]
            resource_names = rule.get("resourceNames", [])

            # Check 3: Every rule with 'patch' verb must have resourceNames
            if "patch" in verbs:
                if not resource_names or not isinstance(resource_names, list) or len(resource_names) == 0:
                    fail(f"Role '{role_name}' has verb 'patch' without resourceNames: {rule}")

            # Check 4: No rule grants secrets, delete, create, or update
            disallowed_verbs = {"delete", "deletecollection", "create", "update"}
            intersected_verbs = disallowed_verbs.intersection(verbs)
            if intersected_verbs:
                fail(f"Role '{role_name}' has disallowed verbs {intersected_verbs}: {rule}")

            if "secrets" in resources or "secret" in resources:
                fail(f"Role '{role_name}' grants access to secrets: {rule}")

    info("RBAC rules verified: no wildcards, patch has resourceNames, no secrets/delete/create/update.")

    # Check 5: Service 'shipit' is ClusterIP
    services = [d for d in docs if d.get("kind") == "Service" and d.get("metadata", {}).get("name") == "shipit"]
    if not services:
        fail("Service 'shipit' not found.")
    for svc in services:
        svc_type = svc.get("spec", {}).get("type", "ClusterIP")
        if svc_type != "ClusterIP":
            fail(f"Service 'shipit' type must be ClusterIP, got: {svc_type}")
    info("Service 'shipit' is type ClusterIP.")

    # Check 7: Deployment 'shipit' has strategy.type == Recreate, replicas == 1, runAsNonRoot
    shipit_deps = [d for d in docs if d.get("kind") == "Deployment" and d.get("metadata", {}).get("name") == "shipit"]
    if not shipit_deps:
        fail("Deployment 'shipit' not found.")
    for dep in shipit_deps:
        spec = dep.get("spec", {})
        strategy = spec.get("strategy", {})
        strat_type = strategy.get("type")
        if strat_type != "Recreate":
            fail(f"Deployment 'shipit' strategy.type must be Recreate, got: {strat_type}")

        replicas = spec.get("replicas")
        if replicas != 1:
            fail(f"Deployment 'shipit' replicas must be 1, got: {replicas}")

        pod_spec = spec.get("template", {}).get("spec", {})
        pod_sec = pod_spec.get("securityContext", {})
        run_as_non_root = pod_sec.get("runAsNonRoot")
        if run_as_non_root is not True:
            # Check containers securityContext as fallback
            containers = pod_spec.get("containers", [])
            all_non_root = containers and all(c.get("securityContext", {}).get("runAsNonRoot") is True for c in containers)
            if not all_non_root:
                fail("Deployment 'shipit' does not set runAsNonRoot: true")
    info("Deployment 'shipit' verified: strategy.type == Recreate, replicas == 1, runAsNonRoot == true.")

    # Check 8: secret.example.yaml contains only REPLACE_ME
    if not SECRET_EXAMPLE_FILE.is_file():
        fail(f"{SECRET_EXAMPLE_FILE} does not exist.")
    with open(SECRET_EXAMPLE_FILE, "r", encoding="utf-8") as f:
        secret_doc = yaml.safe_load(f)
    if not secret_doc or secret_doc.get("kind") != "Secret":
        fail(f"{SECRET_EXAMPLE_FILE} must be a Kubernetes Secret.")
    secret_data = secret_doc.get("stringData") or secret_doc.get("data")
    if not secret_data:
        fail(f"{SECRET_EXAMPLE_FILE} has no stringData or data.")

    expected_keys = {
        "AVINA_STAGING_SHIPIT_DB_URL",
        "SHIPIT_MASTER_KEY",
        "SHIPIT_ADMIN_USER",
        "SHIPIT_ADMIN_PASSWORD",
    }
    missing_keys = expected_keys - set(secret_data.keys())
    if missing_keys:
        fail(f"{SECRET_EXAMPLE_FILE} is missing expected keys: {missing_keys}")

    for k, v in secret_data.items():
        if v != "REPLACE_ME":
            fail(f"{SECRET_EXAMPLE_FILE} key '{k}' must be 'REPLACE_ME', got: {v}")
    info("secret.example.yaml verified: all values are 'REPLACE_ME'.")

    # Check 9: shipit-demo container name is 'app'
    demo_deps = [d for d in docs if d.get("kind") == "Deployment" and d.get("metadata", {}).get("name") == "shipit-demo"]
    if not demo_deps:
        fail("Deployment 'shipit-demo' not found.")
    for dep in demo_deps:
        containers = dep.get("spec", {}).get("template", {}).get("spec", {}).get("containers", [])
        if not containers:
            fail("Deployment 'shipit-demo' has no containers.")
        container_names = [c.get("name") for c in containers]
        if "app" not in container_names:
            fail(f"Deployment 'shipit-demo' must have a container named 'app', got: {container_names}")
    info("Deployment 'shipit-demo' verified: container named 'app'.")

    # Check 10: .dockerignore contains .secrets and .git
    if not DOCKERIGNORE_FILE.is_file():
        fail(f"{DOCKERIGNORE_FILE} does not exist.")
    with open(DOCKERIGNORE_FILE, "r", encoding="utf-8") as f:
        dockerignore_lines = [line.strip() for line in f if line.strip() and not line.startswith("#")]

    required_dockerignore = [".secrets", ".git", "**/bin", "**/obj", "**/node_modules", "tests", "docs", "deploy", "examples", "*.md"]
    for req in required_dockerignore:
        if not any(line == req or line == f"{req}/" or line.startswith(f"{req} ") for line in dockerignore_lines):
            fail(f".dockerignore is missing required entry: {req}")
    info(".dockerignore verified: contains .secrets, .git, and other required exclusions.")

    # Check 11: Grep for any real secrets or unexpected IP addresses in deploy/ and examples/
    ip_pattern = re.compile(r"\b(?:\d{1,3}\.){3}\d{1,3}\b")
    allowed_ips = {"192.168.1.241", "192.168.1.49", "0.0.0.0", "127.0.0.1"}

    for check_dir in (K8S_DIR, REPO_ROOT / "examples"):
        for file_path in check_dir.rglob("*"):
            # Skip build artifacts, git dirs, and binary files
            parts = file_path.parts
            if "bin" in parts or "obj" in parts or ".git" in parts:
                continue
            if file_path.is_file() and not file_path.name.endswith((".dll", ".exe", ".pdb")):
                try:
                    content = file_path.read_text(encoding="utf-8")
                    for match in ip_pattern.finditer(content):
                        ip = match.group(0)
                        if ip not in allowed_ips:
                            fail(f"Unexpected IP '{ip}' found in {file_path}")
                except UnicodeDecodeError:
                    pass

    info("IP/host check verified: only allowed IPs (192.168.1.241, 192.168.1.49, localhost) found.")

    print("\nALL MANIFEST ASSERTIONS PASSED SUCCESSFULLY!")
    sys.exit(0)


if __name__ == "__main__":
    main()
