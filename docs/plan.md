# Shipit – Kế hoạch triển khai

Trạng thái: **ĐÃ DUYỆT, đang thi hành** (2026-11-09). T0–T8 xong và đã ghép vào nhánh cục bộ `integration`; T9a, T9b xong trên staging (ns `devops`). **Đang dừng trước U/T9c**: cần quyết định về mạng pod→slave (`192.168.1.49:22`) và dọn dữ liệu thử trong DB staging; sau đó bạn nhập credential.

## Quyết định đã chốt

| # | Quyết định |
|---|---|
| 1 | Frontend **Vue 3 + TailwindCSS + Lucide Icons**; CSDL **PostgreSQL** (`etc_devops_shipit` trên Postgres **staging**, URL lấy từ biến môi trường `AVINA_STAGING_SHIPIT_DB_URL`); UI/UX thiết kế theo skill `ui-ux-pro-max` |
| 2 | Shipit **giữ credential registry** (mã hoá), tự `docker login` trên slave lúc build |
| 3 | Lấy code bằng **access token của Git** (mã hoá) |
| 4 | Deploy chỉ **set image** |
| 5 | Shipit chạy ở namespace **`devops`** trên cụm **staging** (context `staging-admin@staging`) |
| 6 | Secret (SSH key, token Git, credential registry) mã hoá AES-256-GCM **trong Postgres**; master key trong k8s Secret |
| 7 | Truy cập UI bằng **port-forward** (Service ClusterIP, không LoadBalancer/Ingress); tài khoản admin lấy từ k8s Secret |
| 8 | Chỉ có Claude + agy; agy được chạy với `--dangerously-skip-permissions` |
| 9 | `git init` cục bộ; commit theo dạng `<prefix>: <lower message>` + dòng trống + mô tả; không push |
| 10 | **MVP phải được triển khai thật lên staging và chạy thật** (không chỉ mock). Tôi tự làm mọi thứ có thể; khi cần bạn nhập credential/secret của bạn thì **dừng một lần, sau khi mọi thứ khác hoàn tất**, rồi báo bạn |
| 11 | ns `devops` ở staging; allowlist theo đề xuất; test Postgres theo đề xuất (SQLite + test `pg` tuỳ chọn); registry/docker trên slave đã sẵn sàng |
| 12 | **Backend dùng .NET** (ASP.NET Core, `net10.0`) thay cho Python; mọi thứ khác giữ nguyên |

## 0. Khảo sát hiện trạng

- Repo `Shipit` **trống**, chưa phải git repo, không có AGENTS.md/CLAUDE.md → không có quy ước sẵn, dùng `docs/plan.md`.
- PC điều phối: **.NET SDK 10.0.114** (cùng các bản 8/9) và `dotnet-ef` 10.0.10 đã cài; `node` 24 / `npm` 11; `kubectl` 1.30; `docker` client; không có `helm`. (Các lần trước tôi nói thiếu dotnet và node lỗi là **do tôi gọi sai lệnh**, không phải thiếu công cụ.) Docker daemon cục bộ không chạy (build dùng `build-srv`).
- Hạ tầng hiện có (từ skill `avina-infra-docker-k8s`):
  - Máy build: `192.168.1.49` (docker context `build-srv` = `ssh://devops@192.168.1.49`, Docker 20.10).
  - Registry: `192.168.1.241` (staging dùng `registry-secret`); production dùng `registry.local:5000`.
  - Staging k8s: context `staging-admin@staging`, ns `avina-workspace`; ns `devops` do Shipit tạo (chưa kiểm tra tồn tại). Production: không đụng.
  - Quy ước hiện tại: build `docker build -t 192.168.1.241/<tên>:<sha>` → push → `kubectl set image deployment/<tên> app=<image>` → `rollout status`; container tên `app`.
- Shipit sẽ **tự động hoá đúng quy trình thủ công đó**.

## 1. Mục tiêu và phạm vi

**Mục tiêu:** một app monolith chạy trong k8s, cho cấu hình pipeline trên UI, chạy pipeline = SSH vào slave để build+push image, rồi deploy image đó lên cụm k8s hiện hành, xem trạng thái + log.

**MVP (có):**
- 1 slave (SSH, xác thực bằng private key), kiểm tra kết nối.
- Pipeline gồm 4 bước cố định: `checkout → build → push → deploy`.
- Chạy tay (nút Run), xem lịch sử run, trạng thái từng bước, log gần-realtime.
- Deploy = cập nhật image của 1 Deployment/container (tương đương `kubectl set image` + chờ rollout) qua ServiceAccount trong cụm.
- Lưu secret mã hoá; đăng nhập UI đơn giản.
- **Chạy thật trên staging**: Shipit ở ns `devops`, một lần chạy thật build trên slave `192.168.1.49` và deploy vào sandbox `devops/shipit-demo`.

**Chưa làm (sau MVP):** webhook Git / cron trigger, nhiều slave + hàng đợi/cân tải, helm/kustomize, rollback 1 chạm, phân quyền nhiều người dùng/RBAC, multi-cluster, cache build, thông báo (Slack…), pipeline-as-code (YAML tuỳ ý, song song stage).

## 2. Công nghệ và kiến trúc đề xuất

| Hạng mục | Đề xuất | Lý do |
|---|---|---|
| Backend | **.NET 10 (LTS) – ASP.NET Core** (Minimal APIs, SSE, cookie auth) | Theo yêu cầu của bạn. SDK 10.0.114 và `dotnet-ef` 10.0.10 đã cài sẵn trên PC. Engine chạy trong cùng process (`BackgroundService`) |
| Frontend | **Vue 3 + Vite + TypeScript + TailwindCSS + Lucide (`lucide-vue-next`)**, Pinia, Vue Router; thiết kế theo `ui-ux-pro-max` | Theo yêu cầu; Node 24 / npm 11 có sẵn. Build ra `src/Shipit.Api/wwwroot`, ASP.NET Core phục vụ (`UseStaticFiles` + SPA fallback) → vẫn 1 container monolith |
| SSH | **SSH.NET** (`Renci.SshNet`), bọc sau `ISshExecutor` | Thư viện SSH trưởng thành cho .NET: key auth, đọc output từng dòng, exit code. Bọc sau interface để thay bằng Fake khi test |
| K8s | **KubernetesClient** (NuGet chính thức), in-cluster config, patch Deployment + theo dõi rollout | Không cần kubectl trong image; quyền giới hạn theo namespace |
| Dữ liệu | **PostgreSQL** `etc_devops_shipit`, **EF Core + Npgsql**, migrations; URL từ env `AVINA_STAGING_SHIPIT_DB_URL` (đặt vào k8s Secret). App chấp nhận cả dạng URL `postgres://...` lẫn connection string Npgsql | Theo yêu cầu; không cần PVC; migration additive, chạy lúc khởi động pod (`MigrateAsync`) |
| Log run | Bảng `RunLogs` (theo run, theo dòng); UI nhận qua **SSE** (fallback poll) | Bền qua restart, xem log gần-realtime |
| Secret | **AES-256-GCM** (`System.Security.Cryptography`) trước khi vào Postgres; master key (32 byte, base64) + admin user/password + DB URL nằm trong k8s Secret `shipit-secrets` (ns `devops`) | Không lưu plaintext trong DB/log; GCM phát hiện dữ liệu bị sửa; log được che (mask) |
| Test | **xUnit** + NSubstitute; EF Core **SQLite in-memory** cho test DB; `WebApplicationFactory` cho API; Vitest cho web | Không cần hạ tầng thật; model giữ kiểu portable (không dùng `jsonb`) |
| Đóng gói | 1 Dockerfile multi-stage: node (build web) → `dotnet publish` (sdk:10.0) → runtime `aspnet:10.0` (Debian, user non-root, cổng 8080). Deployment replicas=1 (Recreate) ns `devops`, Service ClusterIP (truy cập `kubectl port-forward`), SA+Role | Chạy trong cụm staging, không mở cổng ra ngoài; image Debian có `bash` để kiểm tra kết nối bằng `kubectl exec` |
| Build trên slave | Qua SSH: `git clone` (token Git truyền qua stdin/biến môi trường tạm, không nằm trong argv/log) → `docker login --password-stdin` → `docker build` → `docker push` → `docker logout` | Credential không để lại trên slave, không lộ trong `ps`/log |

Luồng chạy: `Vue UI → ASP.NET Core API → PipelineRunner (BackgroundService) → ISshExecutor(slave) [clone/build/push] → IK8sDeployer [set image, wait rollout] → ghi trạng thái+log vào Postgres`.

Cấu trúc thư mục dự kiến (mỗi worker sở hữu project riêng → không đụng file nhau):
```
Shipit.sln, global.json (ghim SDK 10.0.x), Directory.Build.props (net10.0, nullable, TreatWarningsAsErrors)
src/Shipit.Core/       # interface ISshExecutor, IK8sDeployer, ILogSink, ISecretProtector; options; DTO (T0)
src/Shipit.Data/       # entity, ShipitDbContext, Migrations/, repository, DbUrlParser (T1)
src/Shipit.Security/   # AesGcmSecretProtector, LogMasker (T1)
src/Shipit.Ssh/        # SshNetExecutor, FakeSshExecutor, RemoteCommandBuilder (T2)
src/Shipit.K8s/        # KubernetesDeployer, FakeK8sDeployer, DeployAllowList (T3)
src/Shipit.Engine/     # PipelineRunner, RunCoordinator, khoá run, khôi phục interrupted (T4)
src/Shipit.Api/        # host: endpoint, cookie auth, SSE, /healthz, /readyz, wwwroot (T5)
tests/Shipit.*.Tests/  # mỗi project một project test; Shipit.E2E.Tests (T8)
web/                   # Vue 3 + Vite + Tailwind source (build → src/Shipit.Api/wwwroot)
deploy/k8s/            # base/ (ns, RBAC, deployment, service) + demo/ (shipit-demo)
examples/              # hello-shipit (app .NET tối thiểu): repo thử cho pipeline thật
scripts/               # dev-run.ps1, create-secrets.ps1|.sh
Dockerfile, docs/
```
Mỗi worker chỉ thêm `PackageReference` vào csproj của chính mình (không dùng Central Package Management để tránh xung đột file).

## 3. Giả định, phạm vi chạm vào staging và điểm dừng

Không còn câu hỏi mở (đổi backend sang .NET ở mục 2 và 4). Bốn câu trước đã chốt: ns `devops` ở **staging**; allowlist theo đề xuất; test trên SQLite + test `pg` tuỳ chọn; registry/docker trên slave đã sẵn sàng.

### 3.1 Giả định (nếu sai, báo tôi trước khi duyệt)

| # | Giả định | Cách tôi kiểm tra |
|---|---|---|
| A1 | DB vẫn tên `etc_devops_shipit` trên Postgres **staging**; `AVINA_STAGING_SHIPIT_DB_URL` đã đặt sẵn trên PC này. Tôi chỉ dùng nó để tạo k8s Secret, **không in giá trị**. App chấp nhận cả dạng URL `postgres://...` lẫn connection string Npgsql. | `[ -n "$AVINA_STAGING_SHIPIT_DB_URL" ]` (chỉ kiểm có/không). Nếu phiên Claude Code không thấy biến này → tôi dừng và báo. |
| A2 | Tôi được tự tạo `shipit-secrets`: DB URL lấy từ env, `SHIPIT_MASTER_KEY` và mật khẩu admin sinh ngẫu nhiên. Mật khẩu admin ghi vào `.secrets/shipit-admin.txt` (gitignore, không in ra chat). Tôi cũng sao `registry-secret` từ `avina-workspace` sang `devops` (qua pipe, không in) để pull image. Muốn tự tạo thì báo, tôi dừng ở bước đó. | `kubectl get secret -o name` (chỉ xem tên) |
| A3 | Allowlist deploy = 4 deployment staging `avina-workspace` (`avina-catalog`, `avina-gateway`, `avina-users`, `avina-content-preview`) **cộng** `devops/shipit-demo` do tôi thêm làm sandbox. Chạy thật chỉ nhắm `shipit-demo`, không đụng service thật. Role chỉ cho `patch` đúng các deployment đó (`resourceNames`). | `kubectl auth can-i ... --as=system:serviceaccount:devops:shipit` |
| A4 | Pipeline thật cần một repo Git thử. Tôi chuẩn bị `examples/hello-shipit/` (Dockerfile + app tối thiểu); bạn tạo repo từ thư mục đó trên Git server của bạn (tôi không push), hoặc chỉ repo khác. | Bạn xác nhận khi nhập pipeline |
| A5 | Pod Shipit ở staging tới được Postgres staging và `192.168.1.49:22`. | `kubectl exec` kiểm TCP và `/readyz` ở T9b |
| A6 | **Production không bị đụng** ở bất kỳ bước nào. Mọi lệnh kubectl dùng `--context staging-admin@staging` và `-n` rõ ràng. | Rà lệnh |
| A7 | Dùng **`net10.0`** (LTS; SDK đã có trên PC). Nếu AvinaWorkspace đang dùng bản khác thì cho tôi biết. | Ở T9a kiểm `mcr.microsoft.com/dotnet/sdk:10.0` và `aspnet:10.0` kéo được trên `build-srv`; nếu không thì lùi về `net8.0` và báo bạn |
| A8 | NuGet (`nuget.org`) tải được từ PC và từ `build-srv` khi `docker build`. | `dotnet restore` ở T0; build image ở T9a. Nếu mạng/proxy chặn → tôi dừng và báo |

### 3.2 Thay đổi sẽ xảy ra trên staging (duyệt kế hoạch = đồng ý các thay đổi này)

| Đối tượng | Thao tác | Task |
|---|---|---|
| Namespace `devops` | Tạo (nếu chưa có); kiểm tra tồn tại trước | T9a |
| `devops`: ServiceAccount `shipit`, Role/RoleBinding (đọc deployment/pod, patch `shipit-demo`) | Tạo | T9a |
| `avina-workspace`: Role + RoleBinding `shipit-deployer` (get/list/watch deployments, pods; `patch` chỉ 4 deployment trong allowlist) | Tạo. **Không sửa** workload sẵn có | T9a |
| `devops`: Secret `shipit-secrets`, bản sao `registry-secret` | Tạo | T9b |
| `devops`: Deployment + Service (ClusterIP) `shipit`; Deployment + Service `shipit-demo` | Tạo | T9b |
| Registry `192.168.1.241` | Push `shipit:<sha>` và `shipit-demo:<sha>` từ `build-srv` | T9a |
| Postgres staging `etc_devops_shipit` | EF Core migrations lúc app khởi động (chỉ thêm bảng) | T9b |
| Pipeline thật (do bạn bấm Run) | Slave `192.168.1.49` clone, build, push; Shipit set image của `shipit-demo` | T9c |

### 3.3 Điểm dừng

Tôi tự làm tới hết T9b rồi **dừng một lần** để báo bạn những việc chỉ bạn làm được (credential của bạn):

1. Mở `kubectl --context staging-admin@staging -n devops port-forward svc/shipit 8080:80`, đăng nhập (mật khẩu ở `.secrets/shipit-admin.txt`).
2. Nhập credential **SSH key** của slave (user trên `192.168.1.49` phải dùng được docker), **token Git**, **user/password registry**.
3. Tạo repo Git từ `examples/hello-shipit/`; tạo Slave và Pipeline trên UI (image `shipit-demo`, đích `devops` / `shipit-demo` / container `app`).
4. Bấm Run (hoặc báo tôi); tôi kiểm chứng T9c.

Ngoài các điểm trên, nếu gặp việc cần secret hay quyết định khác (ví dụ biến env không có, `devops` bị chính sách chặn), tôi cũng dừng và báo.

## 4. Giai đoạn và task

Quy ước chung:
- Backend **.NET 10**, `nullable` bật, `TreatWarningsAsErrors`, xUnit. Lệnh chuẩn: `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`. Frontend Vue 3 + TS: `vitest`, `vue-tsc`, `vite build`.
- Task T0–T8 (offline) dùng **fake/mock**; worker không bao giờ chạm cụm, slave, Postgres hay secret thật. Chỉ Claude làm T9a–T9c.
- **Commit:** `<prefix>: <lower message>` + dòng trống + mô tả. `prefix` ∈ `feat|fix|refactor|test|docs|chore|build`; message viết thường. Ví dụ: `feat: add ssh.net executor` ↵ ↵ `Stream stdout line by line, enforce per-command timeout.` Commit của tôi kết thúc bằng dòng `Co-Authored-By` theo cấu hình hệ thống. agy commit trên nhánh `agent/<task>` của nó, **không push**.
- Interface + **API contract (OpenAPI)** chốt ở T0 để các task song song không xung đột file.

### Giai đoạn 0 – Nền tảng (tuần tự, tôi làm)

**T0 `scaffold`** – `git init` cục bộ (nhánh `main`, commit đầu); `.gitignore` (gồm `bin/`, `obj/`, `.secrets/`, `node_modules/`, build output của `wwwroot`), `.dockerignore` (loại `.git`, `.secrets/`); `Shipit.sln`, `global.json`, `Directory.Build.props`; khung các project `src/*` và `tests/*` đã có project reference; **interface** `ISshExecutor`, `IK8sDeployer`, `ILogSink`, `ISecretProtector` và options cấu hình trong `Shipit.Core` (env `AVINA_STAGING_SHIPIT_DB_URL`, `SHIPIT_MASTER_KEY`, `SHIPIT_ADMIN_USER/PASSWORD`, `SHIPIT_DEPLOY_ALLOWLIST`, cờ `Shipit:UseFakes`); host ASP.NET Core rỗng + `/healthz`; **`docs/api-contract.yaml`** (OpenAPI: slave/credential/pipeline/run/log-SSE/auth); thư mục `web/` trống.
- Được sửa: toàn repo.
- Phụ thuộc: bạn duyệt kế hoạch v4.
- Xong khi: `dotnet build -warnaserror` OK; `dotnet test` pass; `dotnet format --verify-no-changes` sạch; test `WebApplicationFactory` cho `GET /healthz` = 200; test đọc được `docs/api-contract.yaml`; `git log --oneline` đúng quy tắc commit.

### Giai đoạn 1 – MVP offline (T1–T8)

**T1 `data-model`** – `Shipit.Data`: entity `Slave`, `Credential` (loại `ssh_key|git_token|registry`, giá trị mã hoá), `Pipeline` (git url+branch, git token, slave, đường dẫn Dockerfile, tên image, registry+credential, target ns/deployment/container), `Run`, `RunStep`, `RunLog`; `ShipitDbContext`; migration `InitialCreate` (Npgsql, additive); `IDesignTimeDbContextFactory`; `DbUrlParser` (URL `postgres://` ↔ connection string Npgsql); repository. `Shipit.Security`: `AesGcmSecretProtector` (nonce ngẫu nhiên mỗi giá trị, định dạng có version), `LogMasker`.
- Được sửa: `src/Shipit.Data/`, `src/Shipit.Security/`, `tests/Shipit.Data.Tests/`, `tests/Shipit.Security.Tests/`.
- Phụ thuộc: T0.
- Xong khi: `dotnet test tests/Shipit.Data.Tests tests/Shipit.Security.Tests` pass trên SQLite in-memory (giá trị lưu trong DB không phải plaintext; sửa 1 byte ciphertext → giải mã báo lỗi; mask thay secret trong log; `DbUrlParser` đúng với URL có ký tự đặc biệt trong mật khẩu); `dotnet ef migrations script --idempotent -p src/Shipit.Data -o <file tạm>` ra SQL hợp lệ **không cần kết nối DB**; test `pg` skip khi không có `SHIPIT_TEST_PG_URL`.

**T2 `ssh-executor`** – `Shipit.Ssh`: `SshNetExecutor` (key auth, đọc output từng dòng, exit code, timeout, hủy, secret qua stdin) bọc sau adapter kết nối để test được; `FakeSshExecutor`; `RemoteCommandBuilder` dựng lệnh `git clone` (token qua env/stdin, không vào argv), `docker login --password-stdin`, `build`, `push`, `logout` **an toàn** (quote, allowlist ký tự).
- Được sửa: `src/Shipit.Ssh/`, `tests/Shipit.Ssh.Tests/`.
- Phụ thuộc: T0.
- Xong khi: `dotnet test tests/Shipit.Ssh.Tests` pass: payload độc (`;`, `$()`, xuống dòng, dấu nháy) bị chặn hoặc quote đúng; secret không xuất hiện trong chuỗi lệnh; logic timeout/hủy/stream đúng với adapter giả. Test cần sshd thật chỉ chạy khi bạn đặt `SHIPIT_TEST_SSH_*` (mặc định skip). **Lưu ý:** không có SSH server giả trong .NET, nên `SshNetExecutor` với SSH thật chỉ được kiểm chứng ở T9c.

**T3 `k8s-deployer`** – `Shipit.K8s`: `KubernetesDeployer` (patch image đúng container của Deployment, chờ rollout có timeout, trả trạng thái), `FakeK8sDeployer`, `DeployAllowList` (kiểm tra `namespace/deployment` trước khi gọi API).
- Được sửa: `src/Shipit.K8s/`, `tests/Shipit.K8s.Tests/`.
- Phụ thuộc: T0.
- Xong khi: `dotnet test tests/Shipit.K8s.Tests` pass với `HttpMessageHandler` giả: ns/deployment ngoài allowlist bị từ chối **mà không gọi API**; body patch đúng container; rollout timeout trả lỗi.

**T4 `run-engine`** – `Shipit.Engine`: `PipelineRunner` chạy tuần tự `checkout → build → push → deploy`; ghi `RunStep`/`RunLog` qua `ILogSink` (mask secret, giới hạn kích thước); dừng khi bước lỗi; tag = sha ngắn lấy từ checkout; `RunCoordinator` (`BackgroundService` + `Channel`) với khoá 1 run/pipeline; khởi động lại thì run `running` → `failed (interrupted)`; `docker logout` trong `finally`.
- Được sửa: `src/Shipit.Engine/`, `tests/Shipit.Engine.Tests/`.
- Phụ thuộc: T1, T2, T3.
- Xong khi: `dotnet test tests/Shipit.Engine.Tests` pass: thành công (4 bước); build lỗi → deploy không chạy, run `failed`; log không chứa secret giả; run thứ hai cùng pipeline bị từ chối khi run đầu đang chạy; `interrupted` khi khởi động lại.

**T5 `api`** – `Shipit.Api`: hiện thực `docs/api-contract.yaml`: CRUD slave/credential/pipeline (không bao giờ trả giá trị credential), test kết nối slave, trigger run, lấy run, log (SSE), đăng nhập admin (cookie session, so sánh mật khẩu hằng thời gian), `/healthz` (sống) và `/readyz` (DB tới được, không còn migration treo), `MigrateAsync` lúc khởi động, chế độ `Shipit:UseFakes`, phục vụ `wwwroot`.
- Được sửa: `src/Shipit.Api/`, `tests/Shipit.Api.Tests/`.
- Phụ thuộc: T1, T4.
- Xong khi: `dotnet test tests/Shipit.Api.Tests` pass (`WebApplicationFactory`): 401 khi chưa login; response không lộ credential; tạo pipeline → trigger → run `success` với fake; mọi path+method trong contract đều có endpoint; `/readyz` báo đúng.

**T6a `ui-foundation`** – Dùng skill `ui-ux-pro-max` sinh design system → `docs/design.md`; khởi tạo Vite + Vue 3 + TS + Tailwind + `lucide-vue-next` + Pinia + Router (build ra `src/Shipit.Api/wwwroot`); layout, trang Login, API client theo contract có **mock adapter**, component dùng chung (Button, Input, Badge trạng thái, Table, Dialog, LogViewer).
- Được sửa: `web/` (trừ `web/src/views/config/`, `web/src/views/runs/`), `docs/design.md`.
- Phụ thuộc: T0.
- Xong khi: `cd web && npm ci && npm run typecheck && npm run test && npm run build` pass; `npm run dev` (mock) hiển thị Login → layout.

**T6b `ui-config-pages`** – Trang Slave, Credential (nhập secret, không hiển thị lại), Pipeline (form: git, slave, registry, Dockerfile, image, deploy target); validate; nút "Test connection".
- Được sửa: `web/src/views/config/`, `web/src/stores/config*`, test tương ứng.
- Phụ thuộc: T6a.
- Xong khi: `npm run typecheck && npm run test && npm run build` pass; test component: tạo/sửa pipeline gọi đúng API mock, secret không render lại.

**T6c `ui-run-pages`** – Danh sách run, trang run (timeline 4 bước, trạng thái, LogViewer nhận SSE, tự cuộn, tải log), nút Run / Re-run.
- Được sửa: `web/src/views/runs/`, `web/src/stores/runs*`, test tương ứng.
- Phụ thuộc: T6a.
- Xong khi: `npm run typecheck && npm run test && npm run build` pass; test: log SSE giả hiển thị tăng dần, trạng thái bước cập nhật.

**T7 `packaging`** – Giao cho agy, **không có giá trị secret thật**:
- `Dockerfile` multi-stage (node build `web` → `dotnet publish` → `aspnet:10.0`, user non-root, cổng 8080; migration chạy khi app khởi động).
- `deploy/k8s/base/`: Namespace `devops`, ServiceAccount, Role/RoleBinding trong `devops`, Role/RoleBinding `shipit-deployer` trong `avina-workspace` (patch giới hạn `resourceNames` theo allowlist), Deployment (replicas=1, Recreate, `envFrom` Secret `shipit-secrets`, `imagePullSecrets: registry-secret`, probe `/healthz` và `/readyz`), Service **ClusterIP**; **không** PVC/LoadBalancer/Ingress.
- `deploy/k8s/demo/`: Deployment + Service `shipit-demo` (container tên `app`, image `192.168.1.241/shipit-demo:v0`).
- `examples/hello-shipit/`: app .NET tối thiểu + Dockerfile để làm repo thử pipeline.
- `deploy/k8s/secret.example.yaml`: toàn placeholder, không apply.
- Được sửa: `Dockerfile`, `deploy/`, `examples/`, `.dockerignore`.
- Phụ thuộc: T0.
- Xong khi: `kubectl apply --dry-run=client -f deploy/k8s/base/ -f deploy/k8s/demo/` OK; Role không có wildcard `*`; `patch` luôn kèm `resourceNames`; Secret mẫu chỉ có placeholder; `.dockerignore` loại `.secrets/` và `.git`; `dotnet build` ví dụ `examples/hello-shipit` OK. `docker build` chỉ do Claude chạy ở T9a.

**T8 `e2e-mock`** – `tests/Shipit.E2E.Tests` (WebApplicationFactory + fakes, đi qua API); `scripts/dev-run.ps1` chạy host với `Shipit:UseFakes=true` và phục vụ `wwwroot`; `README.md` (env, `kubectl port-forward -n devops svc/shipit 8080:80`, quy trình triển khai Shipit).
- Được sửa: `tests/Shipit.E2E.Tests/`, `scripts/dev-run.ps1`, `README.md`.
- Phụ thuộc: T5, T6b, T6c, T7.
- Xong khi: `dotnet test` toàn solution pass; `pwsh scripts/dev-run.ps1` chạy luồng đầu-cuối với fake (login → tạo credential/slave/pipeline → Run → 4 bước success + log).

### Giai đoạn 2 – Triển khai thật lên staging (Claude, không giao worker)

**T9a `stage-prepare`** – Chuẩn bị hạ tầng và image; chưa cần secret.
- Kiểm tra chỉ đọc: `kubectl --context staging-admin@staging get ns devops` (đã có chưa), biến env có sẵn (chỉ kiểm có/không), image `mcr.microsoft.com/dotnet/sdk:10.0` và `aspnet:10.0` kéo được trên `build-srv` (nếu không, lùi về `net8.0` và báo bạn).
- Áp dụng theo thứ tự: Namespace → RBAC (`devops`, `avina-workspace`) → Service.
- Build và push từ `build-srv` (tag = sha git ngắn): `docker --context build-srv build -t 192.168.1.241/shipit:<sha> .`, `docker --context build-srv push ...`; tương tự `shipit-demo` từ `examples/hello-shipit/`.
- Viết `scripts/create-secrets.ps1|.sh` (tạo `shipit-secrets`, sao `registry-secret`, không in giá trị).
- Phụ thuộc: T8 hoàn tất và đã ghép vào `integration`; bạn duyệt kế hoạch.
- Xong khi: `kubectl --context staging-admin@staging get ns devops` có; `kubectl --context staging-admin@staging -n devops get sa,role,rolebinding,svc`; `kubectl --context staging-admin@staging -n avina-workspace get role,rolebinding shipit-deployer`; `docker --context build-srv images | grep -E 'shipit(-demo)? '` thấy cả hai image; `kubectl auth can-i patch deployments/avina-catalog -n avina-workspace --as=system:serviceaccount:devops:shipit --context staging-admin@staging` = yes, còn `delete deployments` và `get secrets` = no.

**T9b `stage-deploy`** – Tạo Secret, chạy Shipit, migration, kiểm tra kết nối.
- Chạy `scripts/create-secrets` (A2); apply Deployment `shipit` và `shipit-demo`; app tự chạy EF migrations lên DB staging khi khởi động.
- Phụ thuộc: T9a.
- Xong khi: `kubectl --context staging-admin@staging -n devops rollout status deployment/shipit` OK; `port-forward` rồi `curl -s -o /dev/null -w '%{http_code}' localhost:<cổng>/healthz` = 200, `/readyz` = 200 (DB tới được, không còn migration treo) và một route bảo vệ không cookie = 401; `kubectl ... exec deploy/shipit -- bash -c 'exec 3<>/dev/tcp/192.168.1.49/22'` không lỗi (chỉ mở cổng, chưa đăng nhập SSH); `get secret -o name` thấy `shipit-secrets`, `registry-secret`; `rollout status deployment/shipit-demo` OK.
- **Sau T9b tôi dừng** và báo bạn các việc ở mục 3.3.

**Việc của bạn (U)** – Mục 3.3: nhập credential, tạo repo thử, tạo Slave + Pipeline, bấm Run.

**T9c `stage-verify`** – Sau khi bạn báo xong, tôi theo dõi và kiểm chứng run thật (đây cũng là lần đầu `SshNetExecutor` chạy với SSH thật).
- Phụ thuộc: U hoàn tất.
- Xong khi: tiêu chí MVP phần "Thật sau khi bạn nhập credential" (mục 8); mọi lỗi tôi gặp được sửa trên nhánh `agent/<task>` tương ứng rồi build/deploy lại.

### Giai đoạn 3 – Sau MVP
Webhook/cron trigger, nhiều slave + hàng đợi, rollback, apply manifest/helm, thông báo, build cache, đa người dùng. Lập kế hoạch riêng khi MVP xong.

## 5. Bảng phân công

| Task | Agent | Lý do | Nhánh | Chạy |
|---|---|---|---|---|
| T0 scaffold | Claude | Chốt interface + API contract; mọi task khác dựa vào | `agent/scaffold` | Đầu tiên, tuần tự |
| T1 data-model | agy | Cô lập, rõ phạm vi, có test | `agent/data-model` | Song song (đợt A) |
| T2 ssh-executor | agy | Cô lập sau interface | `agent/ssh-executor` | Song song (đợt A) |
| T3 k8s-deployer | agy | Cô lập sau interface | `agent/k8s-deployer` | Song song (đợt A) |
| T7 packaging | agy | Manifest/Dockerfile/example, không secret thật | `agent/packaging` | Song song (đợt A) |
| T6a ui-foundation | agy | Cần skill `ui-ux-pro-max`; độc lập backend nhờ mock | `agent/ui-foundation` | Song song (đợt A) |
| T4 run-engine | Claude | Logic cốt lõi: lỗi, khoá, mask secret | `agent/run-engine` | Sau T1–T3 |
| T6b ui-config-pages | agy | Nhiều file view, ít rủi ro logic | `agent/ui-config-pages` | Sau T6a; song song với T6c (đợt B) |
| T6c ui-run-pages | agy | Thư mục riêng, không đụng T6b | `agent/ui-run-pages` | Sau T6a; song song với T6b (đợt B) |
| T5 api | Claude | Gắn auth + engine, ảnh hưởng bảo mật | `agent/api` | Sau T4 |
| T8 e2e-mock | agy (Claude review) | Test + README | `agent/e2e-mock` | Sau T5, T6b, T6c, T7 |
| T9a stage-prepare | Claude | Đụng cụm staging và registry thật; secret script | `agent/stage-prepare` | Sau T8 |
| T9b stage-deploy | Claude | Đụng secret, DB staging | `agent/stage-deploy` | Sau T9a; **dừng sau T9b** |
| U (bạn) | Bạn | Credential, repo thử, Slave/Pipeline | – | Sau T9b |
| T9c stage-verify | Claude | Kiểm chứng run thật | `agent/stage-verify` | Sau U |

Đợt A có 5 task agy → chia 2 tab (3 + 2 pane). Trong lúc agy làm đợt B (T6b, T6c) tôi làm T4 rồi T5.

Quy tắc phối hợp: agy chạy `--dangerously-skip-permissions` (bạn đã cho phép, kể cả khi hệ thống báo chặn) nên prompt giao việc nêu rõ **file được sửa, file cấm**, không đụng secret/SSH key thật, không chạm cụm/slave/DB, không push. Tôi kiểm `git diff --name-only` nằm trong phạm vi, chạy lại lệnh "Xong khi", rồi mới ghép cục bộ vào nhánh `integration`. **Không push, không merge vào `main`** (do bạn quyết).

## 6. Bố trí pane/tab (tối đa 3 pane/tab, side-by-side)

- Tab `orch`: pane tôi (T0, T4, T5, T9a–c đều làm ở đây).
- Tab `wave-a-1`: `t1-data-model` | `t2-ssh-executor` | `t3-k8s-deployer`.
- Tab `wave-a-2`: `t7-packaging` | `t6a-ui-foundation`.
- Đợt B, tab `wave-b`: `t6b-ui-config` | `t6c-ui-run` (rồi `t8-e2e`). Đóng pane xong việc trước khi mở pane mới.
- Task tiếp nối cùng chủ đề (sửa lỗi sau review) → giữ cuộc trò chuyện agy đó; task khác → khởi động agy mới.
- Chờ worker bằng `herdr wait`/`--wait` chạy foreground; hết timeout thì đọc output pane trước khi giao lại.

## 7. Rủi ro và cách kiểm chứng

| Rủi ro | Giảm thiểu | Kiểm chứng |
|---|---|---|
| Command injection khi dựng lệnh SSH | `shlex.quote`, allowlist ký tự, secret qua stdin | Test T2 payload độc + secret không có trong argv |
| Lộ secret trong log/DB/response | AES-GCM, mask log, API không trả giá trị credential | Test T1/T4/T5; ở T9c grep log pod theo mẫu private key / `Authorization` / `password` |
| Secret lọt vào image hoặc git | `.dockerignore` và `.gitignore` loại `.secrets/`; build context chỉ chứa mã nguồn | `git status` không thấy `.secrets/`; `docker history` không có file secret; review T7 |
| Shipit có quyền k8s quá rộng | Role theo ns, `patch` chỉ theo `resourceNames`; allowlist trong app | `kubectl auth can-i` (patch đúng deployment = yes; delete / get secrets = no) |
| Run thật làm hỏng service thật ở staging | Chạy thật chỉ nhắm sandbox `shipit-demo`; 4 deployment kia chỉ nằm trong allowlist, không được dùng khi thử | Pipeline thử có đích `devops/shipit-demo`; rà lệnh trước khi chạy |
| Pod không tới được Postgres staging hoặc slave (firewall, NetworkPolicy) | Kiểm TCP và `/readyz` ngay ở T9b, trước khi bạn nhập credential | Lệnh `exec` ở T9b |
| Pull image lỗi trong `devops` (thiếu pull secret) | Sao `registry-secret`, đặt `imagePullSecrets` | `rollout status deployment/shipit` |
| Credential còn sót trên slave | `docker logout` trong `finally`; token Git không ghi vào `.git/config`; xoá thư mục clone | Test trình tự lệnh bằng fake (T2/T4); kiểm tra slave ở T9c |
| Migration chạm DB staging | EF migrations additive; DB riêng `etc_devops_shipit`; không TRUNCATE/DELETE/DROP | `/readyz` báo không còn migration treo; review migration T1 bằng `dotnet ef migrations script` |
| Build treo / log vô hạn | Timeout từng bước, giới hạn kích thước log | Test engine |
| Restart giữa lúc run | Đánh dấu `failed (interrupted)` khi khởi động | Test engine |
| agy sửa ngoài phạm vi | Prompt nêu phạm vi; Claude kiểm diff từng nhánh | `git diff --name-only main..agent/<task>` ⊆ phạm vi |
| Fake khác thật (docker 20.10 trên slave, Postgres thật) | T9b/T9c chạy thật; test `pg` tuỳ chọn | Tiêu chí MVP phần "Thật" |
| Đụng nhầm production | Chỉ dùng `--context staging-admin@staging` + `-n`; không bao giờ dùng context production | Rà lệnh trước khi chạy |
| SSH.NET chưa được thử với sshd thật trước T9c | `SshNetExecutor` đặt sau adapter, logic được test bằng adapter giả; có bộ test tuỳ chọn khi bạn cấp `SHIPIT_TEST_SSH_*` tới một sshd tạm | Test T2; lần đầu thật ở T9c, nên sửa nhanh trên nhánh `agent/ssh-executor` |
| NuGet / mạng / image .NET không tải được | Kiểm `dotnet restore` ở T0 và kéo image ở T9a; lùi `net8.0` nếu `10.0` không có | Lệnh restore và `docker pull` trên `build-srv` |
| DB URL sai định dạng (URL vs connection string) | `DbUrlParser` nhận cả hai, test với ký tự đặc biệt trong mật khẩu | Test T1; `/readyz` ở T9b |

Kiểm chứng mỗi đợt: chạy lại lệnh "Xong khi" của từng task trên nhánh của nó, rồi `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`, `cd web && npm run typecheck && npm run test && npm run build` trên `integration` sau khi ghép.

## 8. Tiêu chí xong của MVP

**Offline (Claude chạy trước khi đụng staging):**
1. `dotnet build -warnaserror` OK; `dotnet test` pass; `dotnet format --verify-no-changes` sạch.
2. `cd web && npm ci && npm run typecheck && npm run test && npm run build` pass.
3. `pwsh scripts/dev-run.ps1` → đăng nhập, tạo credential + slave + pipeline, bấm Run → 4 bước `success` và log (fake).
4. `kubectl apply --dry-run=client -f deploy/k8s/base/ -f deploy/k8s/demo/` OK; Role không có `*`; Service là ClusterIP.
5. Không có secret thật trong repo (grep private key / token pattern = 0 kết quả); `git log` đúng quy tắc commit.

**Thật trên staging, phần tôi tự làm (T9a–T9b):**
6. `kubectl --context staging-admin@staging -n devops rollout status deployment/shipit` OK; `shipit-demo` Running.
7. Qua port-forward: `GET /healthz` = 200, `GET /readyz` = 200 (DB staging tới được, migration đã áp dụng) và route bảo vệ không cookie = 401.
8. `kubectl auth can-i` đúng như mục T9a; TCP tới `192.168.1.49:22` từ pod thành công (`bash -c 'exec 3<>/dev/tcp/...'`).

**Thật trên staging, sau khi bạn nhập credential (T9c):**
9. UI "Test connection" slave `192.168.1.49` thành công.
10. Run thật: slave clone bằng token, build, push `192.168.1.241/shipit-demo:<sha>`; `docker --context build-srv images | grep <sha>` có kết quả.
11. Deploy: `kubectl --context staging-admin@staging -n devops get deploy/shipit-demo -o jsonpath='{.spec.template.spec.containers[0].image}'` = image vừa push; `rollout status` hoàn tất; UI hiện run `success` kèm log đầy đủ.
12. Log run và log pod `shipit` không chứa secret (private key, token, password); slave không còn phiên `docker login` của Shipit.
13. Run cố ý lỗi (ví dụ Dockerfile sai) cho trạng thái `failed`, deploy không chạy, `shipit-demo` giữ nguyên image cũ.
