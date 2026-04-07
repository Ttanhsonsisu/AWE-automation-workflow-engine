# Realtime integration (FE)

Tài liệu này mô tả cách FE lấy snapshot ban đầu và nhận realtime qua SignalR với code hiện tại.

## 1) Quy trình khuyến nghị cho màn hình execution detail

1. Gọi API snapshot ban đầu.
2. Render dữ liệu ban đầu lên UI.
3. Mở kết nối SignalR.
4. Join group theo `instanceId`.
5. Nhận các event delta realtime và merge vào state hiện tại.

> Lưu ý: hệ thống distributed nên realtime là eventual consistency, thứ tự event có thể lệch nhẹ.

---

## 2) API snapshot hiện có

### 2.1 Lấy thông tin execution tổng quan
- `GET /api/executions/{id}`

### 2.2 Lấy log execution ban đầu
- `GET /api/executions/{id}/logs`

### 2.3 Lấy context runtime của workflow instance
- `GET /api/workflows/{instanceId}/context`

### 2.4 Lấy chi tiết 1 step
- `GET /api/workflows/{instanceId}/steps/{stepId}`

---

## 3) API submit workflow (đã chạy qua worker)

- `POST /api/workflows`
- Request body:
  - `definitionId`
  - `jobName?`
  - `inputData?`
  - `isTest` (default `false`)
  - `stopAtStepId?`

### Response success (theo envelope chuẩn `ApiController`)
```json
{
  "success": true,
  "data": {
    "message": "Workflow request submitted",
    "trackingId": "guid",
    "instanceId": "guid"
  }
}
```

### Response error
```json
{
  "code": "...",
  "message": "...",
  "type": "..."
}
```

---

## 4) SignalR endpoint và group

### Hub endpoint
- `/hubs/workflow`

### Hub methods
- `JoinWorkflowGroup(instanceId: string)`
- `LeaveWorkflowGroup(instanceId: string)`

FE cần join đúng `instanceId.ToString()` để nhận event của execution tương ứng.

---

## 5) Event realtime FE sẽ nhận

Theo `UiNotificationConsumer`, FE sẽ nhận 3 luồng chính:

### 5.1 Node status delta
Server method: `NodeStatusChanged`

Payload:
```json
{
  "stepId": "string",
  "status": "Running|Completed|Failed|...",
  "timestamp": "datetime"
}
```

Nguồn phát:
- `StepStartedEvent` -> `Running`
- `UiNodeStatusChangedEvent` -> trạng thái node cập nhật

### 5.2 Workflow status delta (tổng thể)
Server method: `WorkflowStatusChanged`

Payload:
```json
{
  "status": "Running|Suspended|Completed|Failed|...",
  "timestamp": "datetime"
}
```

Hiện tại đã có phát ở các case:
- `Suspended`: khi suspend execution
- `Running`: khi resume execution
- `Completed`: khi workflow hoàn tất
- `Failed`: khi workflow fail ở các nhánh terminal

### 5.3 Workflow log stream
Server method: `WorkflowLogReceived`

Payload:
```json
{
  "stepId": "string",
  "level": "Information|Warning|Error|...",
  "message": "string",
  "timestamp": "datetime"
}
```

---

## 6) Gợi ý xử lý FE

- Bật `withAutomaticReconnect()` cho SignalR client.
- De-duplicate event theo key gợi ý:
  - Node status: `(stepId, status, timestamp)`
  - Log: `(stepId, level, message, timestamp)`
- Sau reconnect:
  1) gọi lại API snapshot,
  2) reconcile state,
  3) tiếp tục nghe delta.

---

## 7) Mapping nhanh cho FE state

- `WorkflowStatusChanged` -> cập nhật badge/trạng thái tổng thể execution.
- `NodeStatusChanged` -> cập nhật graph node color/status.
- `WorkflowLogReceived` -> append log terminal/timeline.

Nếu cần, có thể bổ sung endpoint snapshot tổng hợp (single call) cho execution detail để FE giảm số lần gọi API.
