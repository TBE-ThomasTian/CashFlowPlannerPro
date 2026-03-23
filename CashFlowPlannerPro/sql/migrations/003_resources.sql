-- Resources (Mitarbeiter/Freelancer)
CREATE TABLE IF NOT EXISTS resources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    role TEXT,
    availability REAL DEFAULT 1.0,
    hourly_rate REAL DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Projects
CREATE TABLE IF NOT EXISTS projects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_number TEXT,
    name TEXT NOT NULL,
    color TEXT DEFAULT '#3498db',
    start_date DATE,
    end_date DATE,
    budget REAL DEFAULT 0,
    status TEXT DEFAULT 'active',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Resource Allocations (Zuweisungen)
CREATE TABLE IF NOT EXISTS resource_allocations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    resource_id INTEGER NOT NULL,
    project_id INTEGER NOT NULL,
    date DATE NOT NULL,
    hours REAL DEFAULT 8.0,
    notes TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
    UNIQUE(resource_id, project_id, date)
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_allocations_resource ON resource_allocations(resource_id);
CREATE INDEX IF NOT EXISTS idx_allocations_project ON resource_allocations(project_id);
CREATE INDEX IF NOT EXISTS idx_allocations_date ON resource_allocations(date);