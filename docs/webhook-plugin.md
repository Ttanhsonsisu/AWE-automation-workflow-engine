# Webhook Plugin (WebhookTrigger)

Tài liệu này mô tả cách hoạt động và cấu hình `WebhookTrigger` trong hệ thống AWE.

## 1) Mục tiêu

`WebhookTrigger` cho phép workflow được khởi chạy từ HTTP webhook thông qua API Gateway.

- Endpoint mới (route động): `POST /api/webhooks/catch/{**routePath}`
- Endpoint cũ vẫn giữ để tương thích ngược: `POST /api/webhooks/trigger/{definitionId}`

## 2) Cấu hình trong Workflow Definition

Ví dụ step trigger:

```json
{
  "Id": "Webhook_Trigger_357959",
  "Type": "WebhookTrigger",
  "Inputs": {
    "RoutePath": "stripe-payment",
    "SecretToken": "abc123",
    "IdempotencyKeyPath": "header.X-Request-Id"
  },
  "IsConfigured": true,
  "ExecutionMode": "BuiltIn"
}
```

### Ý nghĩa các trường Inputs

- `RoutePath` (bắt buộc): route động để nhận webhook.
- `SecretToken` (không bắt buộc): secret dùng để verify chữ ký webhook.
- `IdempotencyKeyPath` (không bắt buộc): đường dẫn trích key chống tạo instance trùng.

> Lưu ý quan trọng: giá trị `header` đơn lẻ **không phải** cú pháp hợp lệ để trích key từ header. Cần dùng `header.<TênHeader>`.

## 3) Cú pháp IdempotencyKeyPath hỗ trợ

### 3.1 Lấy từ header

- `header.X-Request-Id`
- `$.header.X-Request-Id`

### 3.2 Lấy từ body JSON

- `body.data.id`
- `$.body.data.id`
- `data.id`

### 3.3 Path không hợp lệ

Các trường hợp bị trả `400 BadRequest`:

- `header.` (thiếu tên header)
- path rỗng
- path chứa `..`
- path bắt đầu hoặc kết thúc bằng dấu `.`

## 4) Luồng xử lý tại API Gateway

Khi gọi `POST /api/webhooks/catch/{**routePath}`:

1. Tìm route đang active theo `routePath`.
2. Verify chữ ký (nếu có `SecretToken`).
3. Trích `idempotencyKey` theo `IdempotencyKeyPath`.
4. Nếu key đã tồn tại cho cùng `DefinitionId` thì trả duplicate, không tạo instance mới.
5. Nếu chưa tồn tại, publish `SubmitWorkflowCommand` với:
   - `TriggerSource = Webhook`
   - `TriggerRoutePath = routePath`
   - `IdempotencyKey = giá trị đã trích`
6. Workflow Engine tạo instance và start node tương ứng.

### Context Settings và webhook payload

Khi tạo instance, Workflow Engine ghép `InputData` đã lưu trong workflow (Context Settings)
với JSON body của webhook:

- Context Settings cung cấp các giá trị mặc định.
- Field có trong webhook body sẽ ghi đè field cùng tên trong Context Settings.
- Object lồng nhau được merge đệ quy; array và scalar từ webhook sẽ ghi đè giá trị mặc định.

Kết quả được lưu tại `ContextData.Inputs` và có thể dùng qua
`{{workflow.input.<field>}}` như các trigger khác.

Webhook body là tùy chọn. Nếu request không có body, hệ thống dùng `{}` làm payload
và toàn bộ `ContextData.Inputs` sẽ được lấy từ Context Settings của workflow.

## 5) Verify chữ ký webhook

Hệ thống tự chọn strategy theo header request:

- **Stripe**: header `Stripe-Signature`
- **GitHub**: header `X-Hub-Signature-256`
- **Generic**: header `X-Signature`

Nếu có `SecretToken` nhưng không khớp chữ ký ⇒ `401 Unauthorized`.

Nếu không cấu hình `SecretToken` ⇒ bỏ qua verify (cho phép request đi tiếp).

## 6) Mapping trạng thái response của `/catch/{routePath}`

- `404`: route không tồn tại hoặc không active.
- `401`: sai chữ ký webhook.
- `400`: `IdempotencyKeyPath` không hợp lệ.
- `200` + `Duplicate webhook ignored.`: request trùng.
- `200` + `Webhook received.`: request hợp lệ, đã nhận và publish command.

## 7) Ví dụ gọi API

## 7.1 Request hợp lệ (idempotency theo header)

```bash
curl -X POST "{{host_local}}/api/webhooks/catch/stripe-payment" \
  -H "Content-Type: application/json" \
  -H "X-Request-Id: req_20260418_001" \
  -H "Stripe-Signature: t=1710000000,v1=<stripe_signature>" \
  -d '{"event":"payment.succeeded","data":{"id":"pay_123"}}'
```

## 7.2 Test duplicate

Gọi lại cùng request với cùng `X-Request-Id`:

- Nếu `IdempotencyKeyPath` cấu hình đúng (`header.X-Request-Id`) thì hệ thống trả duplicate.
- Nếu cấu hình sai path (ví dụ `header`) thì key có thể không được trích, dẫn tới vẫn tạo instance mới.

## 7.3 Demo application intake

```bash
curl -X POST "http://localhost:8080/api/webhooks/catch/demo/application-intake" \
  -H "Content-Type: application/json" \
  -H "X-Signature: abc123" \
  -H "X-Request-Id: req-$(date +%s)" \
  -d '{
    "applicationId": "HS-DEMO-001",
    "applicantName": "Nguyen Van A",
    "email": "nguyenvana@example.edu.vn",
    "major": "Artificial Intelligence",
    "gpa": 3.45,
    "englishScore": 720,
    "experienceMonths": 8,
    "priority": "urgent",
    "note": "Demo payload from webhook"
  }'
```

Endpoint `catch` không yêu cầu bearer token ở controller hiện tại. Chỉ gửi
`X-Signature` khi route có cấu hình `SecretToken`; giá trị header phải khớp secret đó.

## 7.4 Chỉ kích hoạt workflow, không gửi data

```bash
curl -X POST "http://localhost:8080/api/webhooks/catch/demo/application-intake" \
  -H "X-Signature: abc123"
```

Trong trường hợp này, workflow chạy hoàn toàn bằng dữ liệu đã lưu trong Context Settings.
Nếu route không cấu hình `SecretToken`, có thể bỏ cả header `X-Signature`.

## 8) Đồng bộ route từ Workflow Definition

Khi publish/update workflow definition:

- Hệ thống quét các step `Type = WebhookTrigger`.
- Tạo mới hoặc update route theo `RoutePath`.
- Route không còn trong definition sẽ bị `Deactivate()`.

`RoutePath` là unique trong DB để tránh map trùng giữa nhiều workflow.

## 9) Khuyến nghị cấu hình

- Luôn đặt `SecretToken` cho webhook public.
- Luôn đặt `IdempotencyKeyPath` rõ ràng:
  - ưu tiên `header.X-Request-Id` cho hệ thống gửi chủ động
  - hoặc `body.<field>` nếu provider không gửi header idempotency
- Chuẩn hóa route ngắn gọn, ổn định theo domain nghiệp vụ (ví dụ `stripe-payment`, `github-pr-opened`).
