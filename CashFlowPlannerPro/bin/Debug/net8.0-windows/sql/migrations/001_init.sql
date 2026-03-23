-- mirrors Database::ensureSchema
CREATE TABLE IF NOT EXISTS categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL);
CREATE TABLE IF NOT EXISTS persons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL);
CREATE TABLE IF NOT EXISTS transactions(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 date TEXT NOT NULL,
 description TEXT,
 amount REAL NOT NULL,
 category_id INTEGER,
 person_id INTEGER,
 interval TEXT,
 notes TEXT,
 created_at TEXT DEFAULT CURRENT_TIMESTAMP,
 updated_at TEXT,
 FOREIGN KEY(category_id) REFERENCES categories(id),
 FOREIGN KEY(person_id) REFERENCES persons(id)
);
CREATE TABLE IF NOT EXISTS offers(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 date_expected TEXT, customer TEXT, amount REAL, probability REAL, description TEXT, status TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS invoices(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 issue_date TEXT, due_date TEXT, customer TEXT, amount REAL, description TEXT, paid_date TEXT, paid_amount REAL, status TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS targets(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 year INTEGER, month INTEGER, amount REAL
);
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);
