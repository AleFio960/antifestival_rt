-- Migration 004 – payment_type_names column + indexes for day-level queries
-- Run AFTER 003_indexes.sql

ALTER TABLE loyverse.receipts
    ADD payment_type_names NVARCHAR(500) NULL;
GO

-- Speeds up the date-range queries used by the web UI
CREATE INDEX IX_receipts_receipt_date
ON loyverse.receipts (receipt_date)
INCLUDE (fiscal_status, payment_cash, payment_electronic, total_money, payment_type_names);
GO
