# Triển khai Shipit lên Production (Namespace `cicd`)

Tài liệu hướng dẫn triển khai hệ thống CI/CD Shipit lên môi trường Kubernetes production tại namespace `cicd`.

## Lưu ý quan trọng
- **Namespace `cicd`:** Đã tồn tại sẵn trên cụm, manifest không tạo Namespace. Tuyệt đối **không** xoá namespace `cicd`.
- **Registry:** Cụm production kéo image từ `registry.local:5000`. Registry nội bộ này mở, không yêu cầu xác thực hay cấu hình secret kéo ảnh. Image Shipit phải được build và push lên `registry.local:5000/cicd-shipit:<tag>` trước khi triển khai.
- **Biến môi trường DB:** Cần thiết lập biến môi trường `AVINA_PROD_SHIPIT_DB_URL` trước khi chạy script tạo secret.

---

## Thứ tự triển khai

### Bước 1: Tạo Kubernetes Secret (`shipit-secrets`)
Secret được tạo thông qua script `scripts/create-secrets-prod.ps1` (giá trị truyền qua stdin, không in ra terminal):

1. Kiểm tra điều kiện tiên quyết (Dry-run mặc định):
   ```powershell
   pwsh scripts/create-secrets-prod.ps1
   ```
2. Thực hiện tạo secret trên cụm (sử dụng cờ `-Apply`):
   ```powershell
   pwsh scripts/create-secrets-prod.ps1 -Apply
   ```
   > [!NOTE]
   > Script sẽ sinh `SHIPIT_MASTER_KEY` (AES-256), mật khẩu quản trị và lưu vào `.secrets/shipit-admin-prod.txt`. Script từ chối ghi đè nếu secret đã tồn tại để tránh làm mất dữ liệu mã hoá.

### Bước 2: Áp dụng RBAC cho ServiceAccount trong namespace `cicd`
```bash
kubectl apply -f deploy/k8s/prod/10-rbac-cicd.yaml
```

### Bước 3: Áp dụng RBAC trong namespace `avina-workspace`
Cấp quyền hạn chế cho ServiceAccount `shipit` thao tác trên 6 Deployment được phép:
```bash
kubectl apply -f deploy/k8s/prod/11-rbac-avina-workspace.yaml
```

### Bước 3.5: Tạo Secret migration (`shipit-migration-secrets`) và chạy Job migration
Ứng dụng Shipit chạy với `SHIPIT_AUTO_MIGRATE=false` và kết nối DB bằng tài khoản ít quyền (không phải table owner). Do đó, trước khi triển khai mới hoặc nâng cấp Shipit, cần chạy Kubernetes Job `shipit-migrate` (`--migrate`) sử dụng tài khoản DB có quyền DDL/owner:

1. Tạo Secret `shipit-migration-secrets` trong namespace `cicd` chứa key `SHIPIT_MIGRATION_DB_URL` (chuỗi kết nối PostgreSQL với quyền owner/migration, truyền qua stdin để không lưu lịch sử shell):
   ```bash
   kubectl -n cicd create secret generic shipit-migration-secrets \
     --from-literal=SHIPIT_MIGRATION_DB_URL='<migration-db-url>'
   ```
2. Chạy Job migration với tag image tương ứng (xoá Job cũ nếu đang nâng cấp):
   ```bash
   kubectl -n cicd delete job shipit-migrate --ignore-not-found
   sed 's/REPLACE_TAG/v1.0.0/g' deploy/k8s/prod/15-migrate-job.yaml | kubectl apply -f -
   kubectl -n cicd wait --for=condition=complete job/shipit-migrate --timeout=120s
   ```

### Bước 4: Triển khai Deployment Shipit
Thay thế chuỗi giữ chỗ `REPLACE_TAG` bằng tag image thực tế (ví dụ: `v1.0.0`):

- **Cách 1 (dùng `sed` kết hợp `kubectl apply`):**
  ```bash
  sed 's/REPLACE_TAG/v1.0.0/g' deploy/k8s/prod/20-deployment.yaml | kubectl apply -f -
  ```
- **Cách 2 (apply trước, cập nhật image sau):**
  ```bash
  kubectl apply -f deploy/k8s/prod/20-deployment.yaml
  kubectl -n cicd set image deployment/shipit app=registry.local:5000/cicd-shipit:v1.0.0
  ```

### Bước 5: Áp dụng Service
Tạo ClusterIP Service nội bộ:
```bash
kubectl apply -f deploy/k8s/prod/30-service.yaml
```

---

## Kiểm tra sau triển khai

1. **Trạng thái Rollout:**
   ```bash
   kubectl -n cicd rollout status deployment/shipit
   ```

2. **Kiểm tra quyền hạn RBAC (`auth can-i`):**
   - Kiểm tra quyền patch trên các deployment được phép (phải trả về `yes`):
     ```bash
     kubectl -n avina-workspace auth can-i patch deployment/avina-catalog --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i patch deployment/avina-content-preview --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i patch deployment/avina-gateway --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i patch deployment/avina-learning --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i patch deployment/avina-sysadmin --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i patch deployment/avina-users --as=system:serviceaccount:cicd:shipit
     ```
   - Kiểm tra quyền quản lý Job và đọc log pod (phải trả về `yes`):
     ```bash
     kubectl -n avina-workspace auth can-i create jobs.batch --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i delete jobs.batch --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i get pods/log --as=system:serviceaccount:cicd:shipit
     ```
   - Kiểm tra các quyền bị từ chối (phải trả về `no`):
     ```bash
     kubectl -n avina-workspace auth can-i patch deployment/avina-forbidden --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i get secrets --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i delete deployments --as=system:serviceaccount:cicd:shipit
     kubectl -n avina-workspace auth can-i create pods --as=system:serviceaccount:cicd:shipit
     kubectl -n cicd auth can-i get pods --as=system:serviceaccount:cicd:shipit
     kubectl -n cicd auth can-i patch deployment/shipit --as=system:serviceaccount:cicd:shipit
     ```
   > [!NOTE]
   > Quyền Job chỉ có ở namespace `avina-workspace`. Danh sách ConfigMap, Secret và namespace Job được dùng do admin cấu hình trong Allowlist của Shipit.

3. **Truy cập ứng dụng:**
   Sử dụng port-forward để kết nối tới Web UI:
   ```bash
   kubectl -n cicd port-forward svc/shipit 8080:80
   ```
   Mở trình duyệt tại `http://localhost:8080` và đăng nhập với tài khoản `admin` (mật khẩu tại `.secrets/shipit-admin-prod.txt`).

---

## Job migration: Secret chuỗi kết nối

Trong namespace `avina-workspace`, hệ thống sử dụng 6 Kubernetes Secret chứa chuỗi kết nối PostgreSQL cho các Job migration (`DbMigrator`):

- `avina-generation-migrate-secret`: key `ConnectionStrings__GenerationDb` (DB: `etc_avina_generation`)
- `avina-billing-migrate-secret`: key `ConnectionStrings__BillingDb` (DB: `etc_avina_billing`)
- `avina-users-migrate-secret`: key `ConnectionStrings__UsersDb` (DB: `etc_avina_users`)
- `avina-learning-migrate-secret`: key `ConnectionStrings__LearningDb` (DB: `etc_avina_learning`)
- `avina-catalog-migrate-secret`: key `ConnectionStrings__CatalogDb` (DB: `etc_avina_catalog`)
- `avina-files-migrate-secret`: key `ConnectionStrings__FilesDb` (DB: `etc_avina_files`)

### Cách chạy script nhập chuỗi kết nối
Script nhập chuỗi kết nối qua `Read-Host -AsSecureString` (không hiển thị lên màn hình, không lưu lịch sử shell hay đối số dòng lệnh, truyền vào `kubectl` qua stdin). Script luôn tự động truyền `--context kubernetes-admin@kubernetes` và `-n avina-workspace` cho mọi lệnh `kubectl`, người điều hành **không cần đổi context hay namespace hiện tại** trong kubeconfig trước khi chạy (chỉ cần context `kubernetes-admin@kubernetes` có sẵn trong kubeconfig):

1. **Kiểm tra trước (Dry-run mặc định):**
   ```powershell
   pwsh scripts/set-migrate-secrets-prod.ps1
   ```
   Hoặc chỉ định một số service:
   ```powershell
   pwsh scripts/set-migrate-secrets-prod.ps1 -Services generation, billing
   ```

2. **Thực hiện cập nhật (với cờ `-Apply`):**
   ```powershell
   pwsh scripts/set-migrate-secrets-prod.ps1 -Apply
   ```
   Nhập chuỗi kết nối khi được yêu cầu (chuỗi phải chứa `Host=`, `Database=<tên DB>`, `Username=`, `Password=` và không còn chữ `CHANGE_ME`). Nhấn Enter để trống nếu muốn bỏ qua service.

### Rollback / Gỡ bỏ Secret
Khi cần xoá hoặc hoàn tác Secret migrate trong `avina-workspace`:
```bash
kubectl -n avina-workspace delete secret <name>
```
Ví dụ:
```bash
kubectl -n avina-workspace delete secret avina-generation-migrate-secret
```

---

## Hướng dẫn Rollback

Khi cần gỡ bỏ bản triển khai, thực hiện lệnh `kubectl delete -f` theo **thứ tự ngược lại**:

```bash
kubectl delete -f deploy/k8s/prod/30-service.yaml
kubectl delete -f deploy/k8s/prod/20-deployment.yaml
kubectl delete -f deploy/k8s/prod/11-rbac-avina-workspace.yaml
kubectl delete -f deploy/k8s/prod/10-rbac-cicd.yaml
```

*(Tuỳ chọn gỡ bỏ secret nếu muốn xoá toàn bộ cấu hình: `kubectl -n cicd delete secret shipit-secrets`)*

> [!CAUTION]
> **Tuyệt đối KHÔNG xoá namespace `cicd`**.
