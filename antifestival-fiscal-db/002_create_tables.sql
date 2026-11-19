CREATE TABLE loyverse.fiscal_log (
    id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    receipt_number  NVARCHAR(50)         NULL,
    request_json    NVARCHAR(MAX)        NULL,
    response_text   NVARCHAR(MAX)        NULL,
    return_code     INT                  NULL,
    created_at      DATETIMEOFFSET       NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO

CREATE TABLE loyverse.fiscal_queue (
    receipt_number          NVARCHAR(50)  NOT NULL PRIMARY KEY,
    status                  NVARCHAR(30)  NOT NULL DEFAULT 'DA_INVIARE',
    attempts                INT           NOT NULL DEFAULT 0,
    last_error              NVARCHAR(MAX) NULL,
    fatturapro_return_code  INT           NULL,
    fatturapro_description  NVARCHAR(MAX) NULL,
    fatturapro_url          NVARCHAR(MAX) NULL,
    inserted_at             DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    sent_at                 DATETIMEOFFSET NULL,
    updated_at              DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO

CREATE TABLE loyverse.receipt_rows (
    id                  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    receipt_number      NVARCHAR(50)   NOT NULL,
    line_id             NVARCHAR(100)  NOT NULL,
    sku                 NVARCHAR(100)  NULL,
    item_id             NVARCHAR(100)  NULL,
    variant_id          NVARCHAR(100)  NULL,
    item_name           NVARCHAR(MAX)  NULL,
    variant_name        NVARCHAR(MAX)  NULL,
    quantity            DECIMAL(18,4)  NULL,
    unit_price          DECIMAL(18,4)  NULL,
    gross_total_money   DECIMAL(18,4)  NULL,
    total_discount      DECIMAL(18,4)  NULL,
    total_money         DECIMAL(18,4)  NULL,
    tax_rate            DECIMAL(18,4)  NULL,
    tax_amount          DECIMAL(18,4)  NULL,
    payload_json        NVARCHAR(MAX)  NULL,
    inserted_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO

CREATE TABLE loyverse.receipts (
    receipt_number      NVARCHAR(50)   NOT NULL PRIMARY KEY,
    receipt_type        NVARCHAR(20)   NULL,
    refund_for          NVARCHAR(50)   NULL,
    receipt_date        DATETIMEOFFSET NULL,
    created_at          DATETIMEOFFSET NULL,
    updated_at          DATETIMEOFFSET NULL,
    cancelled_at        DATETIMEOFFSET NULL,
    store_id            NVARCHAR(100)  NULL,
    pos_device_id       NVARCHAR(100)  NULL,
    employee_id         NVARCHAR(100)  NULL,
    customer_id         NVARCHAR(100)  NULL,
    total_money         DECIMAL(18,4)  NULL,
    total_tax           DECIMAL(18,4)  NULL,
    total_discount      DECIMAL(18,4)  NULL,
    payment_cash        DECIMAL(18,4)  NOT NULL DEFAULT 0,
    payment_electronic  DECIMAL(18,4)  NOT NULL DEFAULT 0,
    payload_json        NVARCHAR(MAX)  NOT NULL,
    fiscal_status       NVARCHAR(30)   NOT NULL DEFAULT 'DA_INVIARE',
    inserted_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    last_sync_at        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO

CREATE TABLE loyverse.sync_logs (
    id           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    service      NVARCHAR(50)   NOT NULL,
    status       NVARCHAR(50)   NOT NULL,
    message      NVARCHAR(MAX)  NOT NULL,
    details_json NVARCHAR(MAX)  NULL,
    created_at   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
