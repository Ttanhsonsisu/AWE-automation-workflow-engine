# Realtime update cho FE (bản cập nhật mới)

Tài liệu này chỉ mô tả **phần cập nhật mới** để FE tích hợp nhanh, không thay thế tài liệu tổng `docs/realtime-intergration.md`.

---

## 1) Cập nhật mới: Workflow status realtime ở trạng thái terminal

Backend đã bổ sung bắn `UiWorkflowStatusChangedEvent` cho các trạng thái kết thúc luồng:

- `Completed`
- `Failed`

### FE cần xử lý
Lắng nghe event SignalR:
- `WorkflowStatusChanged(status, timestamp)`

Trong đó `status` có thể nhận thêm:
- `Completed`
- `Failed`

> Trước đây chủ yếu có `Running`/`Suspended`, nên FE có thể chưa cập nhật badge terminal theo socket.

---

## 2) Cập nhật mới: Realtime retry cho node

Backend đã bổ sung bắn event retry khi engine quyết định retry step.

### Event được bắn
- `UiNodeStatusChangedEvent` với `Status = "Retrying"`
- `WriteAuditLogCommand` với `Event = "StepRetrying"`

### FE cần xử lý
1. Trong handler `NodeStatusChanged`, thêm mapping trạng thái:
   - `Retrying` (ví dụ màu cam/vàng + icon loading)
2. Trong luồng log realtime (`WorkflowLogReceived`), hiển thị log `StepRetrying`.

---

## 3) Gợi ý mapping UI nhanh

### Workflow badge
- `Running` -> đang chạy
- `Suspended` -> tạm dừng
- `Completed` -> hoàn thành
- `Failed` -> thất bại

### Node badge
- `Running`
- `Completed`
- `Failed`
- `Retrying` (mới)

---

## 4) Quy tắc merge state phía FE

Khuyến nghị de-duplicate event theo key:

- Node status: `(stepId, status, timestamp)`
- Workflow status: `(status, timestamp)`
- Log: `(stepId, level, message, timestamp)`

Khi reconnect:
1. gọi snapshot API,
2. reconcile lại state,
3. tiếp tục nhận delta qua socket.

---

## 5) Backward compatibility

Các event cũ không bị thay đổi payload. FE chỉ cần bổ sung xử lý thêm:
- workflow terminal status (`Completed`, `Failed`),
- node status mới `Retrying`.
