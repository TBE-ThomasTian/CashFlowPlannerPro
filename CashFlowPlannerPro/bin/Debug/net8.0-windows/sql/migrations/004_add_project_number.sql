-- Add project_number column to projects table if it doesn't exist
ALTER TABLE projects ADD COLUMN project_number TEXT;