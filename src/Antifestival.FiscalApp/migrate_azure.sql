-- ============================================================
-- Antifestival FiscalApp – Schema Migration
-- Target: loyverse schema + all tables
-- Idempotent: safe to run multiple times
-- ============================================================

-- Schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'loyverse')
    EXEC('CREATE SCHEMA loyverse');
GO

-- ── receipts ─────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name='loyverse' AND t.name='receipts')
BEGIN
    CREATE TABLE loyverse.receipts (
        receipt_number       NVARCHAR(100)    NOT NULL,
        receipt_type         NVARCHAR(50)     NULL,
        refund_for           NVARCHAR(100)    NULL,
        receipt_date         DATETIMEOFFSET   NULL,
        created_at           DATETIMEOFFSET   NULL,
        updated_at           DATETIMEOFFSET   NULL,
        cancelled_at         DATETIMEOFFSET   NULL,
        store_id             NVARCHAR(100)    NULL,
        pos_device_id        NVARCHAR(100)    NULL,
        employee_id          NVARCHAR(100)    NULL,
        customer_id          NVARCHAR(100)    NULL,
        total_money          DECIMAL(18,2)    NULL,
        total_tax            DECIMAL(18,2)    NULL,
        total_discount       DECIMAL(18,2)    NULL,
        payment_cash         DECIMAL(18,2)    NOT NULL DEFAULT 0,
        payment_electronic   DECIMAL(18,2)    NOT NULL DEFAULT 0,
        payment_type_names   NVARCHAR(500)    NULL,
        payload_json         NVARCHAR(MAX)    NULL,
        fiscal_status        NVARCHAR(50)     NOT NULL DEFAULT 'DA_INVIARE',
        last_sync_at         DATETIMEOFFSET   NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_receipts PRIMARY KEY (receipt_number)
    );
    CREATE INDEX IX_receipts_receipt_date   ON loyverse.receipts (receipt_date);
    CREATE INDEX IX_receipts_fiscal_status  ON loyverse.receipts (fiscal_status);
END
GO

-- ── receipt_rows ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name='loyverse' AND t.name='receipt_rows')
BEGIN
    CREATE TABLE loyverse.receipt_rows (
        id                   INT              NOT NULL IDENTITY(1,1),
        receipt_number       NVARCHAR(100)    NOT NULL,
        line_id              NVARCHAR(100)    NOT NULL,
        sku                  NVARCHAR(200)    NULL,
        item_id              NVARCHAR(100)    NULL,
        variant_id           NVARCHAR(100)    NULL,
        item_name            NVARCHAR(500)    NULL,
        variant_name         NVARCHAR(500)    NULL,
        quantity             DECIMAL(18,4)    NULL,
        unit_price           DECIMAL(18,2)    NULL,
        gross_total_money    DECIMAL(18,2)    NULL,
        total_discount       DECIMAL(18,2)    NULL,
        total_money          DECIMAL(18,2)    NULL,
        tax_rate             DECIMAL(10,4)    NULL,
        tax_amount           DECIMAL(18,2)    NULL,
        payload_json         NVARCHAR(MAX)    NULL,
        CONSTRAINT PK_receipt_rows PRIMARY KEY (id),
        CONSTRAINT UQ_receipt_rows_line UNIQUE (receipt_number, line_id)
    );
    CREATE INDEX IX_receipt_rows_receipt_number ON loyverse.receipt_rows (receipt_number);
END
GO

-- ── fiscal_queue ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name='loyverse' AND t.name='fiscal_queue')
BEGIN
    CREATE TABLE loyverse.fiscal_queue (
        id                       INT              NOT NULL IDENTITY(1,1),
        receipt_number           NVARCHAR(100)    NOT NULL,
        status                   NVARCHAR(50)     NOT NULL DEFAULT 'DA_INVIARE',
        attempts                 INT              NOT NULL DEFAULT 0,
        last_error               NVARCHAR(MAX)    NULL,
        fatturapro_return_code   INT              NULL,
        fatturapro_description   NVARCHAR(MAX)    NULL,
        fatturapro_url           NVARCHAR(1000)   NULL,
        inserted_at              DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        updated_at               DATETIMEOFFSET   NULL,
        sent_at                  DATETIMEOFFSET   NULL,
        CONSTRAINT PK_fiscal_queue PRIMARY KEY (id),
        CONSTRAINT UQ_fiscal_queue_receipt UNIQUE (receipt_number)
    );
    CREATE INDEX IX_fiscal_queue_status ON loyverse.fiscal_queue (status);
END
GO

-- ── fiscal_log ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name='loyverse' AND t.name='fiscal_log')
BEGIN
    CREATE TABLE loyverse.fiscal_log (
        id               INT              NOT NULL IDENTITY(1,1),
        receipt_number   NVARCHAR(100)    NOT NULL,
        request_json     NVARCHAR(MAX)    NULL,
        response_text    NVARCHAR(MAX)    NULL,
        return_code      INT              NULL,
        created_at       DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_fiscal_log PRIMARY KEY (id)
    );
    CREATE INDEX IX_fiscal_log_receipt_number ON loyverse.fiscal_log (receipt_number);
    CREATE INDEX IX_fiscal_log_created_at     ON loyverse.fiscal_log (created_at);
END
GO

-- ── sync_logs ────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name='loyverse' AND t.name='sync_logs')
BEGIN
    CREATE TABLE loyverse.sync_logs (
        id           INT              NOT NULL IDENTITY(1,1),
        service      NVARCHAR(100)    NOT NULL,
        status       NVARCHAR(50)     NOT NULL,
        message      NVARCHAR(MAX)    NULL,
        details_json NVARCHAR(MAX)    NULL,
        created_at   DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_sync_logs PRIMARY KEY (id)
    );
    CREATE INDEX IX_sync_logs_created_at ON loyverse.sync_logs (created_at);
END
GO

-- ── app_settings ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name='loyverse' AND t.name='app_settings')
BEGIN
    CREATE TABLE loyverse.app_settings (
        [key]      NVARCHAR(100)  NOT NULL,
        [value]    NVARCHAR(500)  NOT NULL,
        updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_app_settings PRIMARY KEY ([key])
    );
END
GO

-- ── Colonne aggiuntive (idempotent) ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('loyverse.fiscal_queue') AND name = 'demo_requeue_at')
    ALTER TABLE loyverse.fiscal_queue ADD demo_requeue_at DATETIMEOFFSET NULL;
GO

PRINT 'Migration completata con successo.';
