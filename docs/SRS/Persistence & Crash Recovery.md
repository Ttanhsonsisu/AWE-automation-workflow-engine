## **5.1 Core Persistence Model (ERD – Revised)**

*Cập nhật: Bổ sung `JoinBarrier` và chuẩn hóa tên cột Leasing.*

```mermaid
erDiagram
    WorkflowDefinition ||--o{ WorkflowInstance : "instantiates"
    WorkflowInstance ||--o{ ExecutionPointer : "controls"
    WorkflowInstance ||--o{ ExecutionLog : "audits"
    WorkflowInstance ||--o{ JoinBarrier : "synchronizes"
    ExecutionPointer ||--o{ OutboxMessage : "triggers"

    WorkflowDefinition {
        uuid id PK
        string name
        int version
        jsonb definition_json
        boolean is_published
        timestamp created_at
    }

    WorkflowInstance {
        uuid id PK
        uuid definition_id FK
        int definition_version
        string status "Running | Suspended | Completed | Failed | Compensated"
        jsonb context_data
        timestamp start_time
        timestamp last_updated
    }

    ExecutionPointer {
        uuid id PK
        uuid instance_id FK
        string step_id
        string status "Pending | Running | Completed | Failed | Skipped"
        string scope "BranchId List"
        int retry_count
        timestamp leased_until "Thay thế locked_until"
        string leased_by "Worker ID"
    }
    
    JoinBarrier {
        uuid id PK
        uuid instance_id FK
        string step_id
        jsonb arrived_tokens "List of Pointer IDs"
        boolean is_released
    }

    OutboxMessage {
        bigint id PK
        uuid correlation_id
        string message_type
        jsonb payload
        timestamp created_at
        timestamp processed_at
    }

    ExecutionLog {
        bigint id PK
        uuid instance_id
        string node_id
        string event
        jsonb metadata
        timestamp created_at
    }

```

### 🔧 Điểm nâng cấp quan trọng

* **Leasing Fields:** `leased_until`, `leased_by` giúp nhận diện chính xác ai đang giữ task.
* **JoinBarrier:** Bảng phục vụ thuật toán Atomic Join.
* **Status:** Bổ sung `Skipped` và `Compensated`.

---

## **5.2 ExecutionPointer – Single Source of Truth**

> **ExecutionPointer là “Ground Truth” duy nhất của Runtime State**

### Quy tắc bất biến (Invariant):

1. **No Ghost Execution:** Một Pointer chỉ được chạy nếu có `Status=Running` và `LeasedUntil > Now`.
2. **Persistence First:** Mọi thay đổi trạng thái phải được `COMMIT` vào DB trước khi gửi sự kiện đi nơi khác.

---

## **5.3 Safe Dispatch & Transaction Boundary**

### **Nguyên tắc vàng**

> **Không có side-effect nào được phép xảy ra nếu DB chưa commit trạng thái.**

### Trình tự chuẩn:

```mermaid
sequenceDiagram
    participant API as Engine API
    participant DB as PostgreSQL
    participant MQ as RabbitMQ

    API->>DB: BEGIN TRANSACTION
    API->>DB: 1. Update ExecutionPointer
    API->>DB: 2. Insert OutboxMessage (Payload đã Resolve Biến)
    DB-->>API: COMMIT
    
    par Async Dispatch
        API->>MQ: Dispatch Message
    end

```

---

## **5.4 Crash Recovery & Zombie Detection**

### **Zombie Execution Detection**

Một ExecutionPointer được coi là **Zombie** nếu:

* `status = Running`
* `leased_until < NOW()` (Hết hạn Lease)
* Không có Heartbeat mới từ Worker.

### **Chiến lược xử lý (Recovery Job)**

| Trạng thái | Hành động |
| --- | --- |
| **Pending** | Không làm gì (Đợi Worker nhận). |
| **Running + Expired Lease** | **Reset to Pending**, Clear `LeasedBy`. |
| **Completed / Skipped** | Ignore. |
| **Failed** | Áp dụng Retry Policy hoặc Manual Intervention. |

---

## **5.5 Message Redelivery & Idempotency Boundary**

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant W2 as Worker B
    participant DB as PostgreSQL

    MQ->>W2: Redeliver Message (do Worker A crash)
    
    W2->>DB: Check Pointer Status & Lease
    
    alt Lease Expired (Zombie)
        W2->>DB: Acquire New Lease (Running + 30s)
        W2->>W2: Execute Logic
    else Lease Valid (Worker A still running)
        W2->>W2: Skip & Ack (Tránh Race Condition)
    end

```

---

## **5.6 Plugin Idempotency – Ranh giới trách nhiệm**

### **Engine đảm bảo**

* **At-least-once Delivery:** Đảm bảo Message đến được tay Worker.
* **Exactly-once Processing State:** Đảm bảo trạng thái trong DB chỉ chuyển đổi 1 lần duy nhất (nhờ Lease & Versioning).

### **Plugin phải đảm bảo**

* Tự xử lý Idempotency cho các tác vụ side-effect (VD: Gọi API thanh toán phải kèm `RequestId`).
* **Engine KHÔNG thể rollback side-effect bên ngoài** nếu Plugin không hỗ trợ Compensation.

---

## **5.7 Recovery After Full System Downtime**

Ngay cả khi toàn bộ cụm Server (API, Worker, MQ) bị mất điện:

1. Khi khởi động lại, Database là nguồn sự thật duy nhất.
2. **Recovery Job** quét bảng `ExecutionPointers`.
3. Tìm các pointer đang dở dang (`Running` nhưng hết hạn Lease).
4. Reset về `Pending` -> Trigger lại quy trình thực thi tự động.

👉 **Không mất dữ liệu, tự động hồi phục (Self-healing).**

---