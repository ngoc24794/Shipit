# Shipit.Data

Lớp dữ liệu cho Shipit sử dụng Entity Framework Core và Npgsql (PostgreSQL).

## Các lệnh Entity Framework Core

Dự án sử dụng `ShipitDbContextFactory` triển khai `IDesignTimeDbContextFactory<ShipitDbContext>`, cho phép chạy các công cụ `dotnet ef` trực tiếp từ project `Shipit.Data` mà không cần phụ thuộc vào `Shipit.Api` hay kết nối cơ sở dữ liệu thật.

### 1. Tạo Migration mới
```bash
dotnet ef migrations add <MigrationName> -p src/Shipit.Data -s src/Shipit.Data
```

Ví dụ migration ban đầu:
```bash
dotnet ef migrations add InitialCreate -p src/Shipit.Data -s src/Shipit.Data
```

### 2. Xoá Migration gần nhất (chưa áp dụng)
```bash
dotnet ef migrations remove -p src/Shipit.Data -s src/Shipit.Data
```

### 3. Xuất script SQL idempotent (không cần kết nối cơ sở dữ liệu)
```bash
dotnet ef migrations script --idempotent -p src/Shipit.Data -s src/Shipit.Data -o <duong_dan_file.sql>
```
