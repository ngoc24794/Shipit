# Shipit

Shipit là hệ thống CI/CD nội bộ gọn nhẹ phục vụ triển khai ứng dụng: giao diện web cấu hình pipeline, điều phối máy chủ build (slave) thực hiện build và push Docker image qua SSH, sau đó cập nhật image lên Kubernetes cluster.

Backend xây dựng trên **.NET 10** (ASP.NET Core Minimal API, EF Core), frontend xây dựng bằng **Vue 3**, **Vite** và **Tailwind CSS**.

---

## Kiến trúc

Shipit hoạt động theo mô hình điều phối tập trung: người dùng thao tác qua Web UI, backend quản lý hàng đợi và thực thi pipeline qua SSH trên worker nodes (slave), rồi gọi Kubernetes API để rollout image mới.

```text
  ┌─────────────────┐
  │   User Browser  │
  └────────┬────────┘
           │ HTTP (Cookie Auth, REST API, SSE Logs)
           ▼
  ┌──────────────────────────────────────────────────────────┐
  │                   Shipit API Server                      │
  │  (.NET 10, PipelineRunner, RunCoordinator, LogMasker)    │
  └────────┬───────────────────────┬─────────────────────────┘
           │                       │
           │ SSH (Port 22)         │ K8s API (In-Cluster Token)
           ▼                       ▼
  ┌─────────────────┐     ┌──────────────────────────────────┐
  │   Build Slave   │     │        Kubernetes Cluster        │
  │ (Git clone,     │     │ (Rollout Deployment set image)   │
  │  Docker build/  │     │                                  │
  │  Docker push)   │     │                                  │
  └─────────────────┘     └──────────────────────────────────┘
```

---

## Cấu trúc thư mục

```text
.
├── deploy/                     # Cấu hình triển khai Kubernetes
│   └── k8s/
│       ├── base/               # Namespace, RBAC, Deployment, Service chính của Shipit
│       └── demo/               # Ứng dụng mẫu shipit-demo
├── docs/                       # Tài liệu thiết kế kiến trúc, API contract (OpenAPI)
├── examples/
│   └── hello-shipit/           # Ứng dụng ASP.NET Core mẫu để chạy thử pipeline
├── scripts/
│   ├── dev-run.ps1             # Script chạy thử ứng dụng cục bộ ở chế độ fake
│   └── validate-manifests.py   # Script kiểm tra hợp lệ của manifest K8s
├── src/
│   ├── Shipit.Api/             # Web API host, endpoints, cookie auth, SPA static files
│   ├── Shipit.Core/            # Abstractions (ISshExecutor, IK8sDeployer, ISecretProtector)
│   ├── Shipit.Data/            # EF Core DbContext, thực thể (Entities), Migrations
│   ├── Shipit.Engine/          # PipelineRunner, RunCoordinator, xử lý hàng đợi
│   ├── Shipit.K8s/             # Triển khai Kubernetes API client và FakeK8sDeployer
│   ├── Shipit.Security/        # Mã hoá AES-GCM và LogMasker che bí mật
│   └── Shipit.Ssh/             # SSH.NET executor, RemoteCommandBuilder, FakeSshExecutor
├── tests/
│   ├── Shipit.Api.Tests/       # Integration tests cho API
│   ├── Shipit.Core.Tests/      # Unit tests cho Core
│   ├── Shipit.Data.Tests/      # Tests cho tầng dữ liệu và DbUrlParser
│   ├── Shipit.E2E.Tests/       # Bộ test toàn trình (End-to-End) qua HTTP
│   ├── Shipit.Engine.Tests/    # Unit tests cho Engine và PipelineRunner
│   ├── Shipit.K8s.Tests/       # Tests cho DeployAllowList và K8s Deployer
│   ├── Shipit.Security.Tests/  # Tests mã hoá AES-GCM và LogMasker
│   └── Shipit.Ssh.Tests/       # Tests SSH command builder và FakeSshExecutor
├── web/                        # Mã nguồn frontend Vue 3 + TypeScript
├── Dockerfile                  # Multi-stage Dockerfile đóng gói Shipit
└── Shipit.sln                  # Solution .NET 10
```

---

## Chạy thử cục bộ (Fake Mode)

Shipit hỗ trợ chế độ giả lập (`Shipit:UseFakes=true`), cho phép chạy đầy đủ toàn bộ luồng mà không cần kết nối tới cụm Kubernetes, SSH slave hay PostgreSQL thật:

1. **Khởi chạy bằng PowerShell 7:**
   ```powershell
   pwsh scripts/dev-run.ps1
   ```
   *Nếu chỉ muốn chạy backend mà không cần build lại frontend (hoặc frontend đã build):*
   ```powershell
   pwsh scripts/dev-run.ps1 -NoBuildWeb -Port 5000
   ```

   > [!NOTE]
   > Dữ liệu chạy thử cục bộ được lưu trong SQLite file tại thư mục `.dev/shipit-dev.db`. Thư mục `.dev/` mang tính chất cục bộ, không commit vào repository.

2. **Đăng nhập:**
   - Mở trình duyệt tại: `http://localhost:5000`
   - Tài khoản mặc định: `admin` / `admin`

3. **Luồng thử nghiệm:**
   - **Credentials:** Tạo 3 credential (SSH Key, Git Token, Registry Password).
   - **Slaves:** Tạo slave (ví dụ host `192.168.1.49`, port `22`, user `devops`), bấm **Test Connection** để kiểm tra kết nối giả lập.
   - **Pipelines:** Tạo pipeline trỏ tới repository, chọn slave và thông tin image.
   - **Trigger Run:** Bấm **Run** để khởi chạy pipeline, theo dõi 4 bước (`checkout` → `build` → `push` → `deploy`) cùng log thời gian thực qua Server-Sent Events (SSE).

---

## Biến môi trường

| Tên biến | Bắt buộc | Mô tả | Ví dụ |
|---|:---:|---|---|
| `AVINA_STAGING_SHIPIT_DB_URL` | Có (Production/Staging) | URL kết nối PostgreSQL. Bị bỏ qua khi bật `Shipit:UseFakes`. | `postgresql://user:pass@host:5432/dbname` |
| `SHIPIT_MASTER_KEY` | Có (Production/Staging) | Khoá Base64 32 byte dùng để mã hoá AES-GCM mọi credential. | *(Xem cách sinh bên dưới)* |
| `SHIPIT_ADMIN_USER` | Không | Tên tài khoản quản trị viên (mặc định: `admin`). | `admin` |
| `SHIPIT_ADMIN_PASSWORD` | Có (Production/Staging) | Mật khẩu tài khoản quản trị viên. Mặc định `admin` trong fake mode. | `s3cur3-p@ssw0rd!` |
| `SHIPIT_DEPLOY_ALLOWLIST` | Có (Production/Staging) | JSON allowlist quy định namespace và deployment được phép cập nhật. | `{"devops":["shipit-demo"]}` |
| `Shipit:UseFakes` | Không | Cờ bật chế độ giả lập (`true`/`false`). Mặc định: `false`. | `true` |
| `Shipit:SqlitePath` | Không | Đường dẫn file SQLite khi chạy ở chế độ fake. | `.dev/shipit-dev.db` |

### Cách sinh `SHIPIT_MASTER_KEY`:
Khoá master bắt buộc phải có độ dài đúng 32 byte (256 bit) được mã hoá Base64:
- **PowerShell:**
  ```powershell
  $bytes = [byte[]]::new(32); [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes); [Convert]::ToBase64String($bytes)
  ```
- **Bash / Linux:**
  ```bash
  openssl rand -base64 32
  ```

---

## Kiểm thử

> [!WARNING]
> **Quy tắc an toàn khi kiểm thử:** Biến môi trường `AVINA_STAGING_SHIPIT_DB_URL` trỏ tới cơ sở dữ liệu thật. Khi chạy `dotnet test` cục bộ, luôn đảm bảo biến này đã được gỡ bỏ khỏi phiên làm việc để không ảnh hưởng dữ liệu ngoài ý muốn.

### Chạy test Backend (.NET 10):
- **PowerShell:**
  ```powershell
  Remove-Item Env:\AVINA_STAGING_SHIPIT_DB_URL -ErrorAction SilentlyContinue; dotnet test
  ```
- **Bash:**
  ```bash
  env -u AVINA_STAGING_SHIPIT_DB_URL dotnet test
  ```
- **Chạy riêng bộ test E2E:**
  ```powershell
  Remove-Item Env:\AVINA_STAGING_SHIPIT_DB_URL -ErrorAction SilentlyContinue; dotnet test tests/Shipit.E2E.Tests
  ```

### Chạy test Frontend (Vue 3):
```bash
cd web
npm run test
```

---

## Triển khai lên Kubernetes

1. **Đóng gói Docker image:**
   ```bash
   docker build -t 192.168.1.241/shipit:latest .
   docker push 192.168.1.241/shipit:latest
   ```

2. **Cấu hình Secret và Manifests:**
   - Tạo secret Kubernetes cho Shipit dựa theo file mẫu `deploy/k8s/secret.example.yaml`:
     ```yaml
     apiVersion: v1
     kind: Secret
     metadata:
       name: shipit-secrets
       namespace: devops
     type: Opaque
     stringData:
       AVINA_STAGING_SHIPIT_DB_URL: "postgresql://..."
       SHIPIT_MASTER_KEY: "..."
       SHIPIT_ADMIN_USER: "admin"
       SHIPIT_ADMIN_PASSWORD: "..."
       SHIPIT_DEPLOY_ALLOWLIST: '{"devops":["shipit-demo"]}'
     ```
   - Áp dụng manifest hạ tầng cơ bản trong `deploy/k8s/base`:
     ```bash
     kubectl apply -k deploy/k8s/base
     ```
   - Triển khai ứng dụng đích mẫu trong `deploy/k8s/demo`:
     ```bash
     kubectl apply -k deploy/k8s/demo
     ```

3. **Truy cập ứng dụng:**
   Pod Shipit không mở ingress công khai mà được truy cập an toàn qua port-forward:
   ```bash
   kubectl -n devops port-forward svc/shipit 8080:80
   ```
   Sau đó mở `http://localhost:8080` trên trình duyệt.

### Triển khai production (namespace cicd)

Bộ manifest và script triển khai môi trường production được đặt tại thư mục `deploy/k8s/prod/`:
- **Namespace:** Triển khai vào namespace `cicd` (đã có sẵn, manifest không tạo Namespace).
- **RBAC tối thiểu:** ServiceAccount `shipit` ở namespace `cicd` chỉ được phép patch đúng 6 Deployment trong namespace `avina-workspace` (`avina-catalog`, `avina-content-preview`, `avina-gateway`, `avina-learning`, `avina-sysadmin`, `avina-users`).
- **Secret:** Tạo tự động qua script `pwsh scripts/create-secrets-prod.ps1` (kiểm tra trước) và thêm cờ `-Apply` để tạo trên cụm (đọc từ biến môi trường `AVINA_PROD_SHIPIT_DB_URL`).
- **Thứ tự triển khai & kiểm tra:** Xem hướng dẫn chi tiết tại [deploy/k8s/prod/README.md](deploy/k8s/prod/README.md).

---

## Pipeline đầu tiên với `examples/hello-shipit`

Trong thư mục `examples/hello-shipit` có sẵn một ứng dụng ASP.NET Core tối thiểu:

1. **Đẩy mã nguồn lên Git Server:**
   ```bash
   cd examples/hello-shipit
   git init
   git add .
   git commit -m "feat: initial commit"
   git remote add origin https://git.example.com/team/hello-shipit.git
   git branch -M main
   git push -u origin main
   ```

2. **Cấu hình trên giao diện Shipit:**
   - **Git URL:** `https://git.example.com/team/hello-shipit.git`, nhánh `main`.
   - **Slave:** Chọn slave đã tạo (`192.168.1.49`).
   - **Dockerfile:** `Dockerfile` (hoặc đường dẫn dùng dấu `/`, ví dụ `examples/hello-shipit/Dockerfile`).
   - **Thư mục context build (tuỳ chọn):** đường dẫn trong repo dùng làm context khi build; nhập `.` để dùng gốc repo (cần khi Dockerfile nằm ở thư mục con nhưng `COPY` các thư mục ở gốc repo). Để trống thì dùng thư mục chứa Dockerfile.
   - **Host registry khi deploy (tuỳ chọn):** dùng khi cụm k8s gọi registry bằng tên khác với máy build (ví dụ máy build dùng `avina.ai:5000`, cụm dùng `registry.local:5000`). Shipit push tới "Máy chủ Registry", còn image đặt vào Deployment dùng host này. Để trống thì dùng chung.
   - **Image Name:** `shipit-demo`, Registry Host `192.168.1.241`.
   - **Thông tin xác thực Registry:** chọn credential nếu registry yêu cầu đăng nhập; chọn "Không cần đăng nhập" nếu registry mở (khi đó Shipit bỏ qua `docker login`/`logout`).
   - **Deploy:** Namespace `devops`, Deployment `shipit-demo`, Container `app`.

3. **Khởi chạy và kiểm tra:**
   - Nhấn **Run** trên giao diện.
   - Khi hoàn thành, ứng dụng `shipit-demo` trong cụm K8s sẽ được cập nhật phiên bản mới nhất.

---

## Mô hình an toàn (Security Model)

- **Ghi nhớ đăng nhập & Quản lý phiên:** Tuỳ chọn "Ghi nhớ đăng nhập (30 ngày)" tạo cookie bền vững kéo dài 30 ngày (trượt); nếu không chọn, cookie phiên hết hạn khi đóng trình duyệt hoặc tối đa 8 giờ. Khoá Data Protection được lưu bền vững trong CSDL và mã hoá bằng master key. Mọi phiên đăng nhập đều gắn với dấu bảo mật; việc thay đổi mật khẩu admin (`SHIPIT_ADMIN_PASSWORD`) sẽ lập tức làm mất hiệu lực toàn bộ các phiên làm việc cũ.
- **Secret chỉ ghi (Write-only):** Mọi bí mật (SSH private key, Git token, Registry password) được bảo vệ bằng chuẩn mã hoá `AES-256-GCM` với khoá đối xứng `SHIPIT_MASTER_KEY`. Các API trả về không bao giờ để lộ giá trị secret gốc.
- **Che giấu bí mật trong Log (Log Masking):** Cơ chế `LogMasker` tự động phát hiện và thay thế các chuỗi bí mật bằng `***` trên mọi dòng log stdout/stderr cũng như sự kiện Server-Sent Events (SSE).
- **Hạn chế Command Injection:** `RemoteCommandBuilder` kiểm tra định dạng an toàn cho tên nhánh, URL, container name; truyền mật khẩu Git và Docker qua `stdin` hoặc file askpass tạm thời, không đưa bí mật vào tham số dòng lệnh (`argv`).
- **Deploy Allowlist:** Shipit chỉ cho phép cập nhật các deployment nằm trong danh sách trắng `SHIPIT_DEPLOY_ALLOWLIST`. Bất kỳ yêu cầu nào can thiệp ngoài danh sách đều bị chặn ngay tại tầng ứng dụng.
- **RBAC tối thiểu:** ServiceAccount của Shipit trong Kubernetes chỉ được cấp quyền tối thiểu (đọc deployment/pod, patch deployment xác định trong namespace được phép).
