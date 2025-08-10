-- Add PDF attachment fields to invoices and offers tables
ALTER TABLE invoices ADD COLUMN pdf_path TEXT;
ALTER TABLE offers ADD COLUMN pdf_path TEXT;