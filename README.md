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
│   ├── Shipit.Core/            # Abstractions (ISshExecutor, IK8sDeployer, IK8sJobRunner, ISecretProtector)
│   ├── Shipit.Data/            # EF Core DbContext, thực thể (Entities), Migrations
│   ├── Shipit.Engine/          # PipelineRunner, RunCoordinator, điều phối Pipeline và Job
│   ├── Shipit.K8s/             # Triển khai K8s Deployer, Job Runner, AllowList và Fake fakes
│   ├── Shipit.Security/        # Mã hoá AES-GCM và LogMasker che bí mật
│   └── Shipit.Ssh/             # SSH.NET executor, RemoteCommandBuilder, FakeSshExecutor
├── tests/
│   ├── Shipit.Api.Tests/       # Integration tests cho API (Pipeline & Job endpoints)
│   ├── Shipit.Core.Tests/      # Unit tests cho Core
│   ├── Shipit.Data.Tests/      # Tests cho tầng dữ liệu và DbUrlParser
│   ├── Shipit.E2E.Tests/       # Bộ test toàn trình (End-to-End) qua HTTP cho Pipeline & Job
│   ├── Shipit.Engine.Tests/    # Unit tests cho Engine và PipelineRunner
│   ├── Shipit.K8s.Tests/       # Tests cho DeployAllowList, JobAllowList, Deployer và JobRunner
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
   - **Deployment được phép:** Thêm namespace và deployment cho phép cập nhật container image (danh sách được lưu trữ trong DB và có thể khởi tạo từ allowlist ban đầu).
   - **Slaves:** Tạo slave (ví dụ host `192.168.1.49`, port `22`, user `devops`), bấm **Test Connection** để kiểm tra kết nối giả lập.
   - **Pipelines:** Tạo pipeline trỏ tới repository, chọn slave và thông tin image (namespace và deployment có gợi ý từ danh sách deployment được phép).
   - **Trigger Run:** Bấm **Run** để khởi chạy pipeline, theo dõi 4 bước (`checkout` → `build` → `push` → `deploy`) cùng log thời gian thực qua Server-Sent Events (SSE).

---

## Biến môi trường

| Tên biến | Bắt buộc | Mô tả | Ví dụ |
|---|:---:|---|---|
| `AVINA_STAGING_SHIPIT_DB_URL` | Có (Production/Staging) | URL kết nối PostgreSQL. Bị bỏ qua khi bật `Shipit:UseFakes`. | `postgresql://user:pass@host:5432/dbname` |
| `SHIPIT_MASTER_KEY` | Có (Production/Staging) | Khoá Base64 32 byte dùng để mã hoá AES-GCM mọi credential. | *(Xem cách sinh bên dưới)* |
| `SHIPIT_ADMIN_USER` | Không | Tên tài khoản quản trị viên (mặc định: `admin`). | `admin` |
| `SHIPIT_ADMIN_PASSWORD` | Có (Production/Staging) | Mật khẩu tài khoản quản trị viên. Mặc định `admin` trong fake mode. | `s3cur3-p@ssw0rd!` |
| `SHIPIT_DEPLOY_ALLOWLIST` | Không | JSON allowlist dùng làm dữ liệu khởi tạo lần đầu (seed) cho danh sách deployment được phép. Sau khi khởi tạo vào cơ sở dữ liệu, quản lý trực tiếp qua giao diện web hoặc API `/api/deploy-targets`. | `{"devops":["shipit-demo"]}` |
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

## Job (Tài nguyên chạy tác vụ một lần)

### Khái niệm
Khác với Pipeline (triển khai dịch vụ chạy liên tục dưới dạng Kubernetes Deployment bằng cách `set image`), **Job** là tài nguyên dành cho các công cụ chạy-hoàn-tất-rồi-thoát (**run-to-completion**) như migration cơ sở dữ liệu (`Avina.AI.DbMigrator`), nạp dữ liệu mẫu ban đầu (seed data), hoặc dọn dẹp theo đợt.

Một Job trong Shipit được thực thi qua 4 bước:
1. `checkout` (kéo mã nguồn từ Git trên Slave)
2. `build` (đóng gói Docker image trên Slave với BuildKit: `DOCKER_BUILDKIT=1 docker build`)
3. `push` (đẩy image lên Docker Registry)
4. `runjob` (tạo và giám sát các Kubernetes Job tuần tự trong cụm)

Mỗi Job bao gồm danh sách **các tác vụ có thứ tự** (tasks). Trong bước `runjob`, các tác vụ được thực thi tuần tự:
- Mỗi tác vụ tương ứng với 1 Kubernetes Job pod (tham số `args`, nguồn biến môi trường, thời gian timeout).
- Shipit theo dõi pod, stream nhật ký (log) thời gian thực theo từng tác vụ (`taskPosition`), đọc exit code.
- Nếu tác vụ hoàn thành với exit code 0 (`Success`), hệ thống tiếp tục chạy tác vụ tiếp theo.
- Nếu bất kỳ tác vụ nào thất bại (exit code khác 0), lượt chạy lập tức dừng với trạng thái `Failed`, các tác vụ phía sau chuyển sang `Skipped`.

### Tạo Job DbMigrator mẫu qua UI từng bước

Để chạy migration cơ sở dữ liệu cho các dịch vụ Avina (ví dụ: `files` và `users`), thực hiện các bước sau:

1. **Cấu hình Allowlist Job:**
   - Vào mục **Allowlist Job** trên thanh điều hướng (hoặc truy cập `/jobs/allowlist`).
   - Thêm các mục cho phép tương ứng:
     - Loại `Namespace`: namespace `avina-workspace`
     - Loại `ConfigMap`: `avina-files-config` và `avina-users-config` (thuộc namespace `avina-workspace`)
     - Loại `Secret`: `avina-files-secret` và `avina-users-secret` (thuộc namespace `avina-workspace`)
     - Loại `PullSecret`: `registry-secret` (thuộc namespace `avina-workspace`)

2. **Tạo Job mới:**
   - Vào mục **Job** ở thanh điều hướng, chọn **Tạo Job mới**.
   - **Nguồn mã & Máy build:**
     - Tên Job: `avina-dbmigrator`
     - Git URL: `https://git.example.com/team/avina.git`, nhánh: `main`
     - Thông tin xác thực Git: chọn credential Git Token đã cấu hình
     - Máy build: chọn Slave build mong muốn
   - **Đóng gói Docker:**
     - Dockerfile: `docker/Dockerfile.dbmigrator`
     - Thư mục context build: `.` (gốc repo)
     - Tên Image: `avina-dbmigrator`
     - Máy chủ Registry: `192.168.1.241` (kèm Registry Credential nếu cần)
   - **Cấu hình Kubernetes:**
     - Namespace: `avina-workspace`
     - Tên container: `app`
     - Pull Secrets: chọn `registry-secret`
   - **Danh sách tác vụ (Tasks):**
     - *Tác vụ 1:* Nhãn `files`, Args: `migrate --service files`, Nguồn ConfigMap: `avina-files-config`, Nguồn Secret: `avina-files-secret`, Timeout: `1800`s, Giới hạn thử lại: `0`, TTL: `3600`s.
     - *Tác vụ 2:* Nhãn `users`, Args: `migrate --service users`, Nguồn ConfigMap: `avina-users-config`, Nguồn Secret: `avina-users-secret`, Timeout: `1800`s, Giới hạn thử lại: `0`, TTL: `3600`s.
   - **Kiểm tra và Lưu:**
     - Bấm **Kiểm tra cấu hình** (Validate) để backend kiểm tra tính hợp lệ và đối soát với allowlist.
     - Xem tab **Bản xem trước YAML** (Preview YAML) để rà soát manifest K8s được sinh tự động cho từng tác vụ.
     - Bấm **Tạo Job** để hoàn tất lưu trữ.

3. **Khởi chạy và Giám sát:**
   - Trên danh sách Job hoặc trang chi tiết, bấm **Chạy ngay**.
   - Giao diện chuyển đến trang chi tiết lượt chạy:
     - **Timeline:** Hiện 4 bước (`checkout` → `build` → `push` → `runjob`).
     - **Bảng tác vụ:** Liệt kê trạng thái (`success`, `failed`, `skipped`), mã thoát (exit code), thời lượng thực thi của từng tác vụ.
     - **Nhật ký (LogViewer):** Nhận log thời gian thực qua Server-Sent Events (SSE), tự động phân nhóm và hỗ trợ gập/mở hoặc lọc theo từng tác vụ.

### Allowlist Job và cơ chế bảo mật Secret

> [!IMPORTANT]
> **Tại sao Shipit chỉ lưu tên ConfigMap / Secret mà không lưu giá trị?**
>
> Trong Kubernetes, quyền `create jobs` không thể giới hạn theo `resourceNames`. Nếu cho phép người dùng nhập file YAML tuỳ ý, một người dùng có thể tạo Pod mount bất kỳ Secret nhạy cảm nào trong toàn bộ cụm.
>
> Để triệt tiêu rủi ro này, Shipit áp dụng mô hình an toàn đa tầng:
> 1. **Dựng Pod từ mẫu cố định:** Shipit tự sinh manifest K8s Job từ cấu trúc định sẵn trong CSDL, không nhận YAML tuỳ ý từ bên ngoài.
> 2. **Allowlist nghiêm ngặt:** Chỉ các namespace, ConfigMap, Secret và ImagePullSecret đã được quản trị viên cấp phép trong **Allowlist Job** mới được phép đưa vào cấu hình Job. Mọi yêu cầu ngoài allowlist bị chặn ngay tại backend trước khi gọi K8s API.
> 3. **Chỉ lưu tên tham chiếu:** CSDL Shipit chỉ lưu **tên** của ConfigMap/Secret để chuyển tiếp vào thuộc tính `envFrom.configMapRef` / `envFrom.secretRef` của Pod spec. Kubernetes tự nạp giá trị khi khởi chạy pod. Shipit **không bao giờ đọc, giải mã hay lưu giữ giá trị bí mật** của cụm.

### RBAC cần thiết trong cụm Kubernetes

Để chạy Job, ServiceAccount `shipit` cần được cấp Role trong namespace được phép (ví dụ `avina-workspace`):
```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: shipit-job-runner
  namespace: avina-workspace
rules:
  - apiGroups: ["batch"]
    resources: ["jobs"]
    verbs: ["create", "get", "list", "watch", "delete"]
  - apiGroups: [""]
    resources: ["pods"]
    verbs: ["get", "list", "watch"]
  - apiGroups: [""]
    resources: ["pods/log"]
    verbs: ["get"]
```

> [!CAUTION]
> ServiceAccount của Shipit **tuyệt đối không** có quyền `get`, `list` trên tài nguyên `secrets`, và không có quyền `delete` trên `deployments`.

### Giới hạn & Quy tắc an toàn

1. **Chỉ áp dụng trên Staging:** Trong giai đoạn hiện tại, tính năng Job chỉ được triển khai và sử dụng trên cụm Staging trong các namespace được allowlist cho phép.
2. **Không dùng cho các lệnh in mật khẩu:** Các câu lệnh của DbMigrator có hành vi in thông tin xác thực ra màn hình (như `create-admin`, `reset-admin-password`, `reset-admin-totp`) **tuyệt đối không đưa vào Job của Shipit**, vì nhật ký pod sẽ được truyền về và lưu trữ trong bảng `RunLogs`.
3. **Cơ chế chạy lại (Re-run):** Mặc định `backoffLimit = 0` để pod K8s không tự ý khởi động lại khi gặp lỗi logic. Khi một tác vụ bị lỗi, người dùng xem exit code và nguyên nhân trong log, sau đó chủ động bấm **Chạy lại** (Re-run) sau khi sự cố đã được khắc phục.

---

## Mô hình an toàn (Security Model)

- **Ghi nhớ đăng nhập & Quản lý phiên:** Tuỳ chọn "Ghi nhớ đăng nhập (30 ngày)" tạo cookie bền vững kéo dài 30 ngày (trượt); nếu không chọn, cookie phiên hết hạn khi đóng trình duyệt hoặc tối đa 8 giờ. Khoá Data Protection được lưu bền vững trong CSDL và mã hoá bằng master key. Mọi phiên đăng nhập đều gắn với dấu bảo mật; việc thay đổi mật khẩu admin (`SHIPIT_ADMIN_PASSWORD`) sẽ lập tức làm mất hiệu lực toàn bộ các phiên làm việc cũ.
- **Secret chỉ ghi (Write-only):** Mọi bí mật (SSH private key, Git token, Registry password) được bảo vệ bằng chuẩn mã hoá `AES-256-GCM` với khoá đối xứng `SHIPIT_MASTER_KEY`. Các API trả về không bao giờ để lộ giá trị secret gốc.
- **Che giấu bí mật trong Log (Log Masking):** Cơ chế `LogMasker` tự động phát hiện và thay thế các chuỗi bí mật bằng `***` trên mọi dòng log stdout/stderr cũng như sự kiện Server-Sent Events (SSE).
- **Hạn chế Command Injection:** `RemoteCommandBuilder` kiểm tra định dạng an toàn cho tên nhánh, URL, container name; truyền mật khẩu Git và Docker qua `stdin` hoặc file askpass tạm thời, không đưa bí mật vào tham số dòng lệnh (`argv`).
- **Deploy Allowlist & Job Allowlist:** Shipit quản lý danh sách trắng riêng biệt cho Deployment (`SHIPIT_DEPLOY_ALLOWLIST`) và Job (`JobAllowEntry`). Bất kỳ namespace, deployment, configmap hay secret nào nằm ngoài allowlist đều bị từ chối ngay tại tầng ứng dụng trước khi gọi API cụm Kubernetes.
- **An toàn Kubernetes Job Pod:** Pod do Shipit tạo ra luôn tuân thủ security context cố định (`runAsNonRoot: true`, `runAsUser: 1654`, thư mục ghi tạm `/tmp` dùng `emptyDir`, `backoffLimit: 0`, bắt buộc có `activeDeadlineSeconds` và `ttlSecondsAfterFinished`). Khi run bị huỷ hoặc ứng dụng khởi động lại, các Job đang chạy hoặc mồ côi sẽ được đối soát theo nhãn `shipit/run-id` và xoá sạch khỏi cụm.
- **RBAC tối thiểu:** ServiceAccount của Shipit trong Kubernetes chỉ được cấp quyền tối thiểu cần thiết để patch Deployment trong allowlist và điều phối batch Job; hoàn toàn không có quyền đọc nội dung `secrets` trong cụm.
