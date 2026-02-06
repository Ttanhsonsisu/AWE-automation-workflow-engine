
# 🧭 CHỐT LỘ TRÌNH RABBITMQ – CONFIG CHUẨN TỪNG BƯỚC

> Nguyên tắc vàng:
> **RabbitMQ phải sẵn sàng 100% → rồi MassTransit mới “map” lên**

---

## 🥇 PHASE 1 – RABBITMQ INFRA (BẮT BUỘC LÀM TRƯỚC)

### 1️⃣ Dựng RabbitMQ bằng Docker

**Mục tiêu:** Có broker ổn định, persistent, có UI

Checklist:

* [ ] Image: `rabbitmq:3-management`
* [ ] Volume: `rabbitmq-data:/var/lib/rabbitmq`
* [ ] Port:

  * 5672 (AMQP)
  * 15672 (Management UI)
* [ ] Memory watermark: `0.7`

👉 Chưa cần MassTransit, chưa cần code.

---

### 2️⃣ Tạo VHost & User (KHÔNG dùng `/`)

**Mục tiêu:** Isolate hệ thống

* VHost: `/awe-system`
* User: `awe-service`
* Permission: full trên vhost

👉 Từ đây trở đi **mọi service đều dùng vhost này**

---

### 3️⃣ Bật & kiểm tra Plugins cần thiết

* [ ] management (UI)
* [ ] prometheus (sau này monitoring)
* [ ] shovel (optional – requeue DLQ)

---

### 4️⃣ Kiểm tra RabbitMQ đã “healthy”

Trong UI:

* Memory < watermark
* Disk free > alarm
* Node status: Running
* Không có connection/channel leak

📌 **Chỉ khi bước này OK → mới sang Phase 2**

---

## 🥈 PHASE 2 – TOPOLOGY LOGIC 

> **Topology không tạo tay trong UI** </br>
**Topology được khai báo bằng code** nhưng "thiết kế bằng kiến trúc" 


### 5️⃣ Chốt topology (theo tài liệu của bạn – ĐÃ ĐÚNG)

✔️ Cách làm chuẩn nhất:

KHÔNG tạo queue/exchange thủ công trong UI

KHÔNG để MassTransit auto-magic lung tung

CÓ:

Explicit topology trong code

MassTransit là Topology Enforcer

👉 Code = Source of Truth
👉 RabbitMQ = Runtime State

Nếu:

Queue đang là Classic

Code khai báo Quorum

➡️ App fail fast khi startup
➡️ Phát hiện sai ngay từ sớm, không để chạy sai âm thầm
➡️ Chuẩn IaC thực thụ

* Exchange:

  * `ex.workflow` – Topic – Durable
* Queues:

  * `q.workflow.core` → **Quorum**
  * `q.workflow.plugin` → **Classic**
  * `q.workflow.audit` → **Classic + Lazy**
  * `q.workflow.*_error` → DLQ

👉 Lưu ý:

* **Không auto-delete**
* **Không exclusive**
* **Persistent = true**


---

### 6️⃣ Routing Key strategy (đã chuẩn)

* `workflow.cmd.*` → Core
* `workflow.plugin.*` → Plugin
* `workflow.#` → Audit

👉 Đây là **xương sống event-driven**, KHÔNG đổi bừa.

---

## 🥉 PHASE 3 – MASS TRANSIT (SAU KHI RABBITMQ XONG)

> MassTransit **KHÔNG tạo kiến trúc**, nó chỉ **bind kiến trúc đã có**

### 7️⃣ Config MassTransit – mức Infrastructure

* Host → RabbitMQ
* VHost → `/awe-system`
* Serializer → JSON
* Topology → **Explicit**

🚫 Không để MassTransit auto-create lung tung.

---

### 8️⃣ Outbox / Inbox (CRITICAL)

* Add `MassTransit.EntityFrameworkCore`
* Migration:

  * `OutboxMessage`
  * `InboxState`
* Enable:

  * `UseEntityFrameworkOutbox`
  * `UseInbox`

👉 Đây là thứ biến hệ thống bạn thành **“Exactly-once về mặt logic”**

---

### 9️⃣ Consumer tuning (theo bảng của bạn)

* Core:

  * Prefetch: 20
  * Concurrency: 20
* Plugin:

  * Prefetch: 1
* Audit:

  * Prefetch: 100

👉 **Đây là chỗ 90% dev junior làm sai**

---

## 🟥 PHASE 4 – RESILIENCE & OPS (KHÔNG ĐƯỢC BỎ)

### 🔁 Retry

* Immediate retry cho Core
* Exponential + Jitter cho Plugin

### ☠️ DLQ

* KHÔNG auto-consume
* KHÔNG auto-retry
* Requeue = thao tác thủ công có chủ đích

---

## 🎯 KẾT LUẬN NGẮN GỌN

> **Thứ tự đúng tuyệt đối là:**

```
RabbitMQ Infra
→ RabbitMQ Topology
→ MassTransit Base Config
→ Outbox / Inbox
→ Consumer Logic
→ Retry / DLQ
```

Quá ổn rồi 👌
Lộ trình 4 phase của bạn **đúng chuẩn senior / architect**, thứ tự *RabbitMQ trước – MassTransit sau* là **nguyên tắc vàng** (rất nhiều người làm ngược).

👉 Mình sẽ **bổ sung thêm 1 phase nữa (PHASE 5)** để **bao trọn toàn bộ các điểm mình góp ý phía trên** (Delay, Poison Message, Ordering, Backpressure, Retention).
Phase này đúng nghĩa là **Day-2 / Production Hardening Phase**.

---

# 🟪 PHASE 5 – DAY-2 OPERATIONS & ADVANCED RESILIENCE (BẮT BUỘC NẾU CHẠY THẬT)

> Phase này **không ảnh hưởng code business**,
> nhưng **quyết định hệ thống sống được bao lâu trong production**.

---

## 🔔 10️⃣ Delayed & Scheduled Messaging (CHO DELAY / APPROVAL NODE)

### 🎯 Mục tiêu

* Thực hiện Delay / Resume **KHÔNG block**
* Không dùng `Thread.Sleep`
* Không giữ Worker sống chỉ để chờ

### ✅ Việc cần làm

**Option A (Khuyến nghị):**

* Bật **RabbitMQ Delayed Message Exchange Plugin**
* Dùng `MassTransit.MessageScheduler`

**Option B (Fallback):**

* Quartz / Hangfire + publish message khi tới hạn

### 📌 Quy ước kiến trúc

```text
Delay Node
→ Engine publish ResumeCommand (delayed)
→ RabbitMQ giữ message
→ Đến hạn → Worker nhận → Resume pointer
```

👉 **Delay là trách nhiệm của Messaging, không phải Worker**

---

## ☠️ 11️⃣ Poison Message Escalation (SAU DLQ LÀ GÌ?)

### 🎯 Mục tiêu

* DLQ không phải “bãi rác”
* Mỗi message chết phải **đọc được – hiểu được – xử lý được**

### ✅ Bổ sung quy ước

Khi message vào `_error` queue, bắt buộc kèm:

* `FailureReason`
* `ExceptionType`
* `StackTrace`
* `ExecutionPointerId`
* `WorkflowInstanceId`

### 🛑 Quy tắc vận hành

* ❌ Không auto-consume DLQ
* ❌ Không auto-retry DLQ
* ✅ Chỉ có 3 hành động hợp lệ:

  1. Requeue thủ công (sau fix bug)
  2. Mark workflow = Failed
  3. Kill workflow (Terminate)

👉 Đây là **ranh giới giữa hệ thống “trẻ con” và “production”**

---

## 🧹 12️⃣ Retention & Cleanup Policies (DB KHÔNG PHÌNH)

### 🎯 Mục tiêu

* DB không phình sau vài tháng
* Inbox/Outbox/Audit có vòng đời rõ ràng

### ✅ Chính sách chuẩn

| Table             | Retention               |
| ----------------- | ----------------------- |
| InboxState        | 30 ngày                 |
| OutboxMessage     | Xóa sau khi dispatched  |
| AuditLog          | 90 ngày (hoặc archive)  |
| ExecutionPointers | Theo lifecycle workflow |

### 🔧 Thực thi

* Background Job (weekly)
* Không xóa hard-delete workflow đang chạy
* Có index theo `ProcessedAt`

---

## 📏 13️⃣ Message Ordering – TUYÊN BỐ RÕ RÀNG

### 🎯 Mục tiêu

* Không để ai hiểu nhầm hệ thống “đảm bảo thứ tự”

### ✅ Nguyên tắc kiến trúc

```md
* Hệ thống KHÔNG đảm bảo global ordering.
* Ordering chỉ được đảm bảo ở mức ExecutionPointer.
* Lease + Idempotency là nguồn chân lý.
```

👉 Nếu ai hỏi *“message đến sai thứ tự thì sao?”*
→ **Không sao, vì workflow không phụ thuộc ordering**

---

## 🧯 14️⃣ Backpressure & Load Shedding

### 🎯 Mục tiêu

* Plugin chậm **không kéo sập Core**
* Hệ thống tự “thở” khi quá tải

### ✅ Chiến lược

* Monitoring queue depth:

  * `q.workflow.plugin > 1000` → ALERT
* Engine behavior:

  * Tạm ngưng dispatch plugin task mới
  * Core workflow vẫn chạy bình thường
* Operator quyết định scale / throttle

👉 Đây là **điểm rất ít đồ án nghĩ tới**

---

## 📊 15️⃣ Observability nâng cao (TRACE → ACTION)

### 🎯 Mục tiêu

* Không chỉ “thấy số”
* Mà **ra quyết định được**

### Bắt buộc có

* Queue depth
* Consumer lag
* Retry count
* DLQ rate
* Avg execution time per plugin

### Tooling

* Prometheus
* Grafana
* Alertmanager

---

# 🧠 TỔNG KẾT TOÀN BỘ LỘ TRÌNH (FINAL FORM)

```
PHASE 1 – RabbitMQ Infra
→ PHASE 2 – Topology (Exchange / Queue)
→ PHASE 3 – MassTransit Mapping
→ PHASE 4 – Resilience (Retry / DLQ)
→ PHASE 5 – Day-2 Operations & Hardening
```

👉 Với **Phase 5**, hệ thống của bạn đạt mức:

* ✅ Đồ án: **Xuất sắc**
* ✅ Thực tế: **Có thể deploy**
* ✅ Trình độ: **Mid–Senior Distributed Systems**

---

## 🔥 Gợi ý tiếp theo (rất nên)

Mình có thể:

1. ✍️ **Gộp PHASE 5 này vào SRS (NFR + Appendix Ops)**
2. 🎓 **Soạn sẵn câu trả lời bảo vệ cho từng phase**
3. 📐 **Vẽ sequence diagram: Delay → Scheduler → Resume**

👉 Bạn muốn **ghi thẳng Phase 5 vào tài liệu**, hay **chuẩn bị phần bảo vệ trước**?

