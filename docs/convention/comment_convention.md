# 📘 COMMENT CONVENTION

**Automation Workflow Engine (AWE)**

## 1. Mục đích (Purpose)

Tài liệu này định nghĩa chuẩn comment trong source code nhằm:

* Tăng khả năng đọc hiểu và bảo trì lâu dài
* Làm rõ các quyết định thiết kế (design decisions)
* Đảm bảo tính nhất quán trong toàn bộ codebase
* Hỗ trợ review, audit và viết thesis/technical report

---

## 2. Nguyên tắc cốt lõi (Core Principles)

> **Code mô tả WHAT – Comment mô tả WHY**

Comment **không** dùng để:

* Lặp lại nội dung của code
* Giải thích cú pháp hiển nhiên
* Diễn giải business rule đã rõ

Comment **bắt buộc** phải:

* Giải thích lý do thiết kế
* Nêu rõ ràng ràng buộc (constraints)
* Làm rõ hệ quả nếu thay đổi (trade-offs)

---

## 3. Ngôn ngữ & phong cách

* **Ngôn ngữ**: English (bắt buộc)
* **Văn phong**: kỹ thuật, ngắn gọn, trung lập
* **Thì**: hiện tại
* **Không dùng** từ mơ hồ: `important`, `careful`, `magic`, `hack`

❌ `// very important logic`
✅ `// Required to prevent parallel state mutation`

---

## 4. Comment cấp File / Class

### 4.1 Khi nào cần

* Core domain
* Infrastructure
* Consumer / Saga / State machine
* Message contracts (Command / Event)

### 4.2 Chuẩn sử dụng

```csharp
/// <summary>
/// Consumer definition for job execution commands.
/// </summary>
/// <remarks>
/// - Controls concurrency to protect workflow state
/// - Uses retry and outbox for reliable processing
/// - Backed by RabbitMQ quorum queue
/// </remarks>
public class JobExecutionConsumerDefinition
{
}
```

* `<summary>`: mô tả chức năng chính (WHAT)
* `<remarks>`: mô tả quyết định thiết kế (WHY)

---

## 5. Comment cấp Method

### 5.1 Public / Protected method

Bắt buộc dùng XML comment.

```csharp
/// <summary>
/// Executes a workflow step.
/// </summary>
/// <remarks>
/// Must be idempotent and retry-safe.
/// </remarks>
```

### 5.2 Private method

Chỉ comment khi method tồn tại vì **lý do thiết kế đặc biệt**.

```csharp
// Isolated to centralize retryable persistence logic
private async Task SaveStateAsync()
{
}
```

---

## 6. Inline Comment (trong thân code)

### 6.1 Không được comment

* Vòng lặp
* Gán biến
* Logic hiển nhiên

❌

```csharp
count++; // increment count
```

### 6.2 Bắt buộc comment khi liên quan đến

* Concurrency
* Retry
* Side-effect
* Messaging semantics
* Resource protection

✅

```csharp
// Enforce strict serial execution to protect non-thread-safe plugins
consumerConfigurator.UseConcurrencyLimit(1);
```

---

## 7. Comment cho Performance & Concurrency

Phải trả lời được:

1. Tại sao chọn giá trị này?
2. Nếu thay đổi thì rủi ro gì?
3. Phụ thuộc assumption nào?

```csharp
// Limit prefetch to avoid pulling more messages than can be processed.
// Prevents memory pressure and uneven workload distribution.
endpointConfigurator.PrefetchCount = 20;
```

---

## 8. Comment cho Resilience (Retry / Outbox)

Không dùng từ chung chung như “handle errors”.

```csharp
// Immediate retries handle short-lived failures
// such as database locks or brief network interruptions.
endpointConfigurator.UseMessageRetry(r => r.Immediate(5));
```

```csharp
// In-memory outbox ensures outgoing messages are published
// only once during consumer retries.
endpointConfigurator.UseInMemoryOutbox(context);
```

---

## 9. Comment cho Messaging & Topology

Bắt buộc làm rõ:

* Queue type (classic / quorum / lazy)
* Exchange
* Routing key

```csharp
// Use quorum queue to guarantee durability and high availability.
// Handles job submission commands routed with "workflow.job.submit".
```

---

## 10. TODO / NOTE / FIXME

### Chuẩn bắt buộc

```csharp
// TODO[AWE-PLUGIN]: Consider isolating plugins by type
// NOTE: Plugins are expected to be time-bounded
// FIXME: This logic breaks when retry overlaps execution
```

* Phải có **context**
* Ưu tiên gắn ticket / module

---

## 11. Những điều cấm kỵ

* Comment lặp lại code
* Comment lỗi thời
* Comment cảm tính
* Trộn tiếng Việt – tiếng Anh trong cùng file

---

## 12. Tóm tắt ngắn gọn

```text
- Comment explains WHY, not WHAT
- XML comments for public APIs
- Inline comments only for non-obvious logic
- Always document concurrency, retry, and messaging decisions
- English only
```

