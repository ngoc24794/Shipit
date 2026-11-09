# Triển khai Shipit lên Production (Namespace `cicd`)

Tài liệu hướng dẫn triển khai hệ thống CI/CD Shipit lên môi trường Kubernetes production tại namespace `cicd`.

## Lưu ý quan trọng
- **Namespace `cicd`:** Đã tồn tại sẵn trên cụm, manifest không tạo Namespace. Tuyệt đối **không** xoá namespace `cicd`.
- **Registry:** Cụm production kéo image từ `registry.local:5000`. Registry nội bộ này mở, không yêu cầu xác thực hay cấu hình secret kéo ảnh. Image Shipit phải được build và push lên `registry.local:5000/shipit:<tag>` trước khi triển khai.
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

### Bước 4: Triển khai Deployment Shipit
Thay thế chuỗi giữ chỗ `REPLACE_TAG` bằng tag image thực tế (ví dụ: `v1.0.0`):

- **Cách 1 (dùng `sed` kết hợp `kubectl apply`):**
  ```bash
  sed 's/REPLACE_TAG/v1.0.0/g' deploy/k8s/prod/20-deployment.yaml | kubectl apply -f -
  ```
- **Cách 2 (apply trước, cập nhật image sau):**
  ```bash
  kubectl apply -f deploy/k8s/prod/20-deployment.yaml
  kubectl -n cicd set image deployment/shipit app=registry.local:5000/shipit:v1.0.0
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
   - Kiểm tra các quyền bị từ chối (phải trả về `no`):
     ```bash
     kubectl -n avina-workspace auth can-i patch deployment/avina-forbidden --as=system:serviceaccount:cicd:shipit
     kubectl -n cicd auth can-i get pods --as=system:serviceaccount:cicd:shipit
     kubectl -n cicd auth can-i patch deployment/shipit --as=system:serviceaccount:cicd:shipit
     ```

3. **Truy cập ứng dụng:**
   Sử dụng port-forward để kết nối tới Web UI:
   ```bash
   kubectl -n cicd port-forward svc/shipit 8080:80
   ```
   Mở trình duyệt tại `http://localhost:8080` và đăng nhập với tài khoản `admin` (mật khẩu tại `.secrets/shipit-admin-prod.txt`).

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
