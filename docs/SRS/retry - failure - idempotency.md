
# 4. RETRY – FAILURE – IDEMPOTENCY (REVISED)

## 4.1 Sequence – Retry thành công (Transient Fault)

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant W as Worker
    participant DB as PostgreSQL
    participant API as External Service

    MQ->>W: Deliver Message (Node X)
    W->>DB: Load ExecutionPointer
    DB-->>W: Status = Pending
    W->>DB: Acquire Lease (Running + 30s)

    W->>API: Call External Service
    API-->>W: 503 Service Unavailable

    Note over W: Throw RetryableException
    W->>DB: Release Lease (Reset to Pending) -- Optional optimization
    W-->>MQ: Nack (Requeue)

    Note over MQ: Backoff Delay (2s)

    MQ->>W: Redeliver Message
    W->>DB: Load ExecutionPointer
    DB-->>W: Status = Pending
    
    rect rgb(200, 255, 200)
        Note right of W: MUST Re-Acquire Lease
        W->>DB: Acquire Lease (Running + 30s)
    end

    W->>API: Retry Call
    API-->>W: 200 OK

    W->>DB: Update Status = Completed
    W->>MQ: Ack
```

---

## 4.2 Retry Exhausted → Failure (FIXED RESPONSIBILITY)

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant FC as Fault Consumer
    participant DB as PostgreSQL
    participant Engine as Engine API

    MQ->>FC: Fault Message (Retry Exhausted)
    FC->>DB: Mark ExecutionPointer = Failed
    FC->>DB: Persist Error Metadata
    FC->>Engine: Publish WorkflowFailed Event

    Engine->>DB: Start Compensation Saga (if any)
```

👉 **Worker không tham gia bước này**

---

## 4.3 Idempotency Flow (Correct Order)
- graph v1
```mermaid
flowchart TD
    A[Worker receives message] --> B[Query DB ExecutionPointer]
    B -- Status != Pending --> C[Ack & Skip]
    B -- Status = Pending --> D[Acquire Lease]
    D -- Failed --> C
    D -- Success --> E[Execute Plugin Logic]

    E -- Success --> F[Update Status = Completed]
    F --> G[Ack]

    E -- Failure --> H[Throw Exception]
```

- graph v2 
```mermaid 
flowchart TD
    A[Worker receives message] --> B{Check DB Pointer}
    
    B -- "Status != Pending<br/>(Already Processed)" --> C[Ack & Skip]
    B -- "Status = Pending" --> D["Acquire Lease<br/>(Running + 30s)"]
    
    D -- Failed (Locked by other) --> C
    D -- Success --> E[Execute Plugin Logic]
    
    subgraph Execution Phase
        E -- "Input: Static Data" --> E1[Run Async Task]
        E1 -.->|Every 10s| E2[Update Heartbeat]
        E2 -.->|Extend Lease| D
    end

    E1 -- Success --> F[Update Status = Completed]
    F --> G[Ack]

    E1 -- Failure --> H["Throw Exception<br/>(Nack / Retry)"]
```

---

## 4.4 Idempotency Guarantees (FINAL)

| Scenario            | Protection      |
| ------------------- | --------------- |
| Duplicate message   | DB status check |
| Parallel workers    | Lease + status  |
| Crash while running | Lease timeout   |
| Retry after success | Idempotent skip |
| Resume double click | Status guard    |
| Data consistency    |  Pre-dispatch Resolution (Engine side) |



