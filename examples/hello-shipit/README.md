# Hello Shipit

Ứng dụng ASP.NET Core tối thiểu dùng làm repo thử nghiệm cho pipeline của Shipit.

## Khởi tạo Git repo từ thư mục này

1. Khởi tạo Git repository cục bộ:
   ```bash
   cd examples/hello-shipit
   git init
   git add .
   git commit -m "feat: initial commit"
   ```

2. Đẩy lên Git server của bạn:
   ```bash
   git remote add origin <git-repo-url>
   git branch -M main
   git push -u origin main
   ```

## Chạy cục bộ

```bash
dotnet run
```

Truy cập:
- `GET http://localhost:8080/` -> `{ "app": "hello-shipit", "version": "dev", "time": "..." }`
- `GET http://localhost:8080/healthz` -> `ok`
