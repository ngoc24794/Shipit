# Shipit UI design system

Nguồn: skill `ui-ux-pro-max` (`--design-system`, density 8 = dashboard dày, variance 3 = tối giản, motion 3 = tinh tế), đã chỉnh cho ứng dụng nội bộ (không phải landing page). Stack: Vue 3 + TailwindCSS + `lucide-vue-next`.

## 1. Hướng thẩm mỹ
Minimalism / Swiss, công cụ vận hành dày thông tin nhưng dễ quét. **Dark mặc định**, có light; theo `prefers-color-scheme` và có nút chuyển (lưu lựa chọn trong `localStorage`, bọc `try/catch`). Không emoji, không gradient trang trí; biểu tượng chỉ dùng Lucide (SVG). Ngôn ngữ giao diện: **tiếng Việt**.

## 2. Màu (CSS variables, dùng qua Tailwind theme, không hard-code hex trong component)
| Token | Dark | Light |
|---|---|---|
| `--bg` | `#0F172A` | `#F8FAFC` |
| `--card` | `#1B2336` | `#FFFFFF` |
| `--muted` (nền phụ) | `#272F42` | `#F1F5F9` |
| `--fg` | `#F8FAFC` | `#0F172A` |
| `--muted-fg` | `#94A3B8` | `#475569` |
| `--border` | `#475569` (dùng ở 50% alpha) | `#CBD5E1` |
| `--primary` (nút chính, mục đang chọn) | `#22C55E` | `#15803D` |
| `--on-primary` | `#0F172A` | `#FFFFFF` |
| `--info` (đang chạy) | `#38BDF8` | `#0369A1` |
| `--warn` (đang chờ) | `#FBBF24` | `#B45309` |
| `--danger` | `#EF4444` | `#DC2626` |
| `--ring` (focus) | `#FFFFFF` | `#2563EB` |

Trạng thái luôn có **nhãn chữ + biểu tượng**, không chỉ dựa vào màu:
`queued` Đang chờ (warn, `Clock`) · `running` Đang chạy (info, `Loader` quay nhẹ) · `success` Thành công (primary, `CheckCircle2`) · `failed` Thất bại (danger, `XCircle`) · `skipped` Bỏ qua (muted, `MinusCircle`) · `pending` Chưa chạy (muted, `Circle`).
Độ tương phản chữ thường >= 4.5:1 ở cả hai theme; viền focus luôn hiện (`outline 2px var(--ring)` + offset 2px), không bao giờ bỏ outline.

## 3. Chữ
- UI: **Fira Sans** (400/500/600/700). Mã, log, tên image, sha, đường dẫn: **Fira Code** (400/500).
- Nạp từ Google Fonts bằng `<link>` kèm `display=swap`; fallback `ui-sans-serif, system-ui` / `ui-monospace, Consolas`. Fira Code không chắc đủ ký tự tiếng Việt: log chủ yếu ASCII, chấp nhận fallback monospace cho dấu.
- Cỡ: nền 14px cho bảng/mật độ cao, 16px cho form/input (tránh zoom trên mobile) và đoạn văn; tiêu đề trang 20–24px/600; nhãn nhỏ (badge, cột) tối thiểu 12px. Line-height 1.5 cho văn bản, 1.4 cho bảng.

## 4. Khoảng cách và bố cục (density 8)
Thang: 4 · 8 · 12 · 16 · 24 · 32. Bo góc 6px (control) / 8px (card). Bóng nhẹ hoặc không bóng; phân tách bằng viền 1px.
- Khung: sidebar trái 232px (thu gọn thành thanh trên + menu trên màn hình < 768px), header 48px (tên trang, nút chuyển theme, người dùng, Đăng xuất), nội dung `max-w-[1280px]` canh giữa, padding 16–24px.
- Mục sidebar (Lucide): Lượt chạy (`Play`), Pipeline (`Workflow`), Máy build (`Server`), Thông tin xác thực (`KeyRound`).
- Bảng: hàng cao 40px, header dính (sticky) khi cuộn, `tabular-nums` cho số/thời gian, hover nhẹ; trên mobile bọc `overflow-x-auto`.
- Mobile-first, kiểm tra ở 375 / 768 / 1024 / 1440; không cuộn ngang trang. Vùng bấm >= 44px trên màn hình cảm ứng (`pointer: coarse`), desktop tối thiểu 32px.

## 5. Component nền tảng (tên gợi ý, thư mục `web/src/components/`)
- `AppButton` (primary / secondary / ghost / danger; trạng thái loading hiện spinner và vô hiệu hoá, nhãn không đổi độ rộng), `AppInput`, `AppSecretInput` (type password, nút hiện/ẩn, **không bao giờ điền lại giá trị cũ**, placeholder "Đã lưu — để trống để giữ nguyên"), `AppSelect`, `AppField` (nhãn hiển thị luôn, lỗi ngay dưới ô, mô tả phụ).
- `StatusBadge` (một dòng, `white-space: nowrap`, icon + nhãn), `AppTable`, `EmptyState` (icon + câu giải thích + nút hành động chính), `AppDialog` (focus trap, Esc đóng, trả focus), `ConfirmDialog` cho hành động xoá, `AppToast` (thành công/lỗi, vùng `role="status"`).
- `StepTimeline`: 4 bước ngang (Checkout `GitBranch`, Build `Hammer`, Push `Upload`, Deploy `Rocket`), nối nhau bằng đường kẻ; mỗi bước có trạng thái, thời lượng; dọc trên mobile.
- `LogViewer`: nền tối cả ở theme sáng (`--log-bg`), Fira Code 13px, số dòng (`seq`), màu theo luồng (stdout thường, stderr tông danger, system chữ mờ nghiêng), **tự cuộn theo log mới** và dừng khi người dùng cuộn lên (nút "Theo dõi" bật lại), nút Sao chép / Tải xuống, ảo hoá hoặc giới hạn số dòng hiển thị để không giật khi log lớn, `role="log"` + `aria-live="off"` (tránh đọc từng dòng), vùng trạng thái riêng `role="status"` báo "Run đã kết thúc: thành công/thất bại".

## 6. Tương tác và chuyển động (motion 3)
Chuyển trạng thái 150–200ms `ease-out`, hover chỉ đổi màu/viền (không dịch bố cục), mọi phần tử bấm được có `cursor-pointer`. Spinner của `running` là animation duy nhất lặp lại. Tôn trọng `prefers-reduced-motion` (tắt mọi chuyển động trang trí, spinner thành biểu tượng tĩnh). Không animate width/height.

## 7. Form và phản hồi
Nhãn luôn hiển thị (không dùng placeholder làm nhãn), lỗi hiện ngay dưới ô và có `aria-describedby`, validate khi blur rồi khi submit; nút gửi hiển thị loading → toast thành công/lỗi. Xoá luôn có xác nhận và nói rõ hệ quả (ví dụ credential đang dùng → báo 409 bằng thông điệp dễ hiểu).

## 8. Nội dung tiếng Việt (từ vựng thống nhất)
Máy build (Slave) · Thông tin xác thực (Credential): loại `Khoá SSH`, `Token Git`, `Registry` · Pipeline · Lượt chạy (Run) · Chạy ngay · Chạy lại · Kiểm tra kết nối · Nhật ký · Theo dõi · Đăng nhập / Đăng xuất. Thông báo lỗi nói rõ nguyên nhân và bước tiếp theo, không lộ chi tiết nhạy cảm.

## 9. Trước khi giao (checklist)
Không emoji làm icon · mọi icon-only button có `aria-label` · focus nhìn thấy được bằng bàn phím · tương phản 4.5:1 ở cả hai theme · không cuộn ngang ở 375px · `prefers-reduced-motion` được tôn trọng · không đặt secret vào DOM/`localStorage` · bảng và log không làm vỡ layout khi nội dung rất dài (`overflow-wrap`, `min-width: 0`).
