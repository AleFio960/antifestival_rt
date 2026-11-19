CREATE UNIQUE INDEX IX_receipt_rows_receipt_line
ON loyverse.receipt_rows (receipt_number, line_id);
GO

CREATE INDEX IX_fiscal_queue_status
ON loyverse.fiscal_queue (status, attempts, inserted_at);
GO

CREATE INDEX IX_receipts_fiscal_status
ON loyverse.receipts (fiscal_status);
GO

CREATE INDEX IX_fiscal_log_receipt_number
ON loyverse.fiscal_log (receipt_number, created_at DESC);
GO
