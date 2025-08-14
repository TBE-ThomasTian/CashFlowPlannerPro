#include "Database.h"
#include <QSqlQuery>
#include <QSqlError>
#include <QVariant>
#include <QDate>
#include <QDebug>
#include <cmath>
#include <QMap>

static QString monthLabel(int y,int m){return QString("%1-%2").arg(y,4,10,QChar('0')).arg(m,2,10,QChar('0'));}
static QDate addMonthsClamped(const QDate& d,int months){int y=d.year()+(d.month()-1+months)/12;int m=(d.month()-1+months)%12+1;int last=QDate(y,m,1).daysInMonth();int day=std::min(d.day(),last);return QDate(y,m,day);}

Database& Database::instance(){ static Database d; return d; }
Database::Database(){}

bool Database::open(const QString& path){ 
    if(m_db.isValid()&&m_db.isOpen()) {
        m_db.close();
    }
    m_db=QSqlDatabase::addDatabase("QSQLITE"); 
    m_db.setDatabaseName(path); 
    if(!m_db.open()){ 
        qCritical()<<"DB open error:"<<m_db.lastError().text(); 
        return false;
    } 
    QSqlQuery q(m_db); 
    q.exec("PRAGMA journal_mode=WAL;"); 
    q.exec("PRAGMA foreign_keys=ON;"); 
    return true; 
}

void Database::close() {
    if(m_db.isValid() && m_db.isOpen()) {
        m_db.close();
    }
}

QSqlDatabase Database::db(){ return m_db; }

void Database::ensureSchema(){
    QSqlQuery q(m_db);
    
    // Create users table
    q.exec(R"(CREATE TABLE IF NOT EXISTS users(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        username TEXT UNIQUE NOT NULL,
        password_hash TEXT NOT NULL,
        full_name TEXT,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
    );)");
    
    // Create default admin user if no users exist
    q.exec("SELECT COUNT(*) FROM users");
    if(q.next() && q.value(0).toInt() == 0) {
        // Create default user "admin" with password "admin"
        // In production, use proper password hashing!
        q.exec("INSERT INTO users (username, password_hash, full_name) VALUES ('admin', 'admin', 'Administrator')");
    }
    
    q.exec(R"(CREATE TABLE IF NOT EXISTS categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL);)");
    q.exec(R"(CREATE TABLE IF NOT EXISTS persons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL);)");
    q.exec(R"(CREATE TABLE IF NOT EXISTS transactions(
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
    );)");
    q.exec(R"(CREATE TABLE IF NOT EXISTS offers(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        offer_number TEXT,
        offer_date TEXT,
        date_expected TEXT,
        customer TEXT,
        amount REAL,
        probability REAL,
        description TEXT,
        status TEXT,
        payment_delay INTEGER DEFAULT 30,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
    );)");
    
    // Add columns if they don't exist (for existing databases)
    q.exec("PRAGMA table_info(offers)");
    bool hasOfferNumber = false;
    bool hasOfferDate = false;
    while(q.next()) {
        QString colName = q.value(1).toString();
        if(colName == "offer_number") {
            hasOfferNumber = true;
        } else if(colName == "offer_date") {
            hasOfferDate = true;
        }
    }
    if(!hasOfferNumber) {
        q.exec("ALTER TABLE offers ADD COLUMN offer_number TEXT");
    }
    if(!hasOfferDate) {
        q.exec("ALTER TABLE offers ADD COLUMN offer_date TEXT");
    }
    q.exec(R"(CREATE TABLE IF NOT EXISTS invoices(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        issue_date TEXT,
        due_date TEXT,
        customer TEXT,
        amount REAL,
        description TEXT,
        paid_date TEXT,
        paid_amount REAL,
        status TEXT,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
    );)");
    q.exec(R"(CREATE TABLE IF NOT EXISTS targets(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        year INTEGER,
        month INTEGER,
        amount REAL
    );)");
    q.exec(R"(CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT);)");
    
    // Add payment_delay column if it doesn't exist (for existing databases)
    q.exec("ALTER TABLE offers ADD COLUMN payment_delay INTEGER DEFAULT 30");
    
    // Add PDF attachment columns if they don't exist
    q.exec("ALTER TABLE invoices ADD COLUMN pdf_path TEXT");
    q.exec("ALTER TABLE offers ADD COLUMN pdf_path TEXT");
    
    // Create resources tables
    q.exec(R"(CREATE TABLE IF NOT EXISTS resources(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT NOT NULL,
        role TEXT,
        availability REAL DEFAULT 1.0,
        hourly_rate REAL DEFAULT 0,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
    );)");
    
    q.exec(R"(CREATE TABLE IF NOT EXISTS projects(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        project_number TEXT,
        name TEXT NOT NULL,
        color TEXT DEFAULT '#3498db',
        start_date TEXT,
        end_date TEXT,
        budget REAL DEFAULT 0,
        status TEXT DEFAULT 'active',
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
    );)");
    
    // Add project_number column if it doesn't exist (for existing databases)
    q.exec("PRAGMA table_info(projects)");
    bool hasProjectNumber = false;
    while(q.next()) {
        QString colName = q.value(1).toString();
        if(colName == "project_number") {
            hasProjectNumber = true;
            break;
        }
    }
    if(!hasProjectNumber) {
        q.exec("ALTER TABLE projects ADD COLUMN project_number TEXT");
    }
    
    q.exec(R"(CREATE TABLE IF NOT EXISTS resource_allocations(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        resource_id INTEGER NOT NULL,
        project_id INTEGER NOT NULL,
        date TEXT NOT NULL,
        hours REAL DEFAULT 8.0,
        notes TEXT,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE,
        FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
        UNIQUE(resource_id, project_id, date)
    );)");
    
    q.exec("CREATE INDEX IF NOT EXISTS idx_allocations_resource ON resource_allocations(resource_id)");
    q.exec("CREATE INDEX IF NOT EXISTS idx_allocations_project ON resource_allocations(project_id)");
    q.exec("CREATE INDEX IF NOT EXISTS idx_allocations_date ON resource_allocations(date)");
    
    // Populate default categories for Fixkosten if they don't exist
    QStringList categories = {
        "Lohn", 
        "Kapitalsteuer", 
        "Sozialversicherung", 
        "Lohnsteuer", 
        "Umsatzsteuer", 
        "Versicherung", 
        "Miete", 
        "Strom",
        "Steuerberatung"
    };
    
    for(const QString& category : categories) {
        q.prepare("INSERT OR IGNORE INTO categories(name) VALUES(:name)");
        q.bindValue(":name", category);
        q.exec();
    }
    
    if(q.lastError().isValid() && !q.lastError().text().contains("duplicate column")){ 
        qWarning()<<"Schema error:"<<q.lastError().text(); 
    }
}

double Database::settingStartBalance(){ QSqlQuery q(m_db); q.prepare("SELECT value FROM settings WHERE key='start_balance'"); if(q.exec()&&q.next()) return q.value(0).toDouble(); return 0.0; }
void Database::setSettingStartBalance(double v){ QSqlQuery q(m_db); q.prepare("INSERT INTO settings(key,value) VALUES('start_balance',:v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;"); q.bindValue(":v",v); q.exec(); }

QMap<QString,double> Database::targets(){ QMap<QString,double> out; QSqlQuery q(m_db); if(q.exec("SELECT year,month,amount FROM targets")){ while(q.next()){ out[monthLabel(q.value(0).toInt(), q.value(1).toInt())]=q.value(2).toDouble(); } } return out; }

QVector<MonthRow> Database::monthlyCashflow(int horizonMonths,bool includeOffersOffen,bool includeOffersBeauftragt,bool includeUnpaidInvoices,bool includeRecurring){
    struct Ev{QDate d; double a;}; QList<Ev> evs;
    QSqlQuery q(m_db);
    q.exec("SELECT date,amount,COALESCE(interval,''),notes FROM transactions");
    while(q.next()){
        QString date_s = q.value(0).toString();
        QDate d = QDate::fromString(date_s, Qt::ISODate);
        if(!d.isValid()) {
            d = QDate::fromString(date_s, "dd.MM.yyyy");
        }
        if(!d.isValid()) {
            d = QDate::fromString(date_s, "yyyy-MM-dd");
        }
        double a=q.value(1).toDouble();
        QString it=q.value(2).toString().toLower().trimmed();
        QString notes=q.value(3).toString();
        
        // Include FIXKOSTEN and STEUER entries in recurring calculation
        bool isFixkosten = notes.startsWith("FIXKOSTEN:");
        bool isSteuer = notes.startsWith("STEUER:");
        
        qDebug() << "Transaction:" << date_s << "->" << d << a << "Interval:" << it << "Notes:" << notes << "isFixkosten:" << isFixkosten << "isSteuer:" << isSteuer;
        
        if(!includeRecurring || it.isEmpty() || it=="once"||it=="einmalig"){ 
            evs.push_back({d,a}); 
        }
        else{
            QDate cur=d; auto push=[&](const QDate&dd){ evs.push_back({dd,a}); };
            push(cur);
            // Support German interval names
            int stepM = 0;
            if(it=="monthly"||it=="monatlich") stepM=1;
            else if(it=="quarterly"||it=="vierteljährlich") stepM=3;
            else if(it=="semiannual"||it=="semi-annually"||it=="halbjahr"||it=="halbjährlich") stepM=6;
            else if(it=="yearly"||it=="jährlich") stepM=12;
            else if(isFixkosten) stepM=1; // Default to monthly for Fixkosten only
            else if(isSteuer) stepM=12; // Default to yearly for Steuer
            
            int stepD = (it=="biweekly")?14:(it=="weekly")?7:0;
            QDate end=addMonthsClamped(QDate::currentDate(),horizonMonths); 
            end=QDate(end.year(),end.month(),QDate(end.year(),end.month(),1).daysInMonth());
            
            // Avoid infinite loop by limiting iterations
            int maxIterations = horizonMonths * 31; // Maximum days in horizon
            int iterations = 0;
            while(iterations < maxIterations){ 
                if(stepM>0) cur=addMonthsClamped(cur,stepM); 
                else if(stepD>0) cur=cur.addDays(stepD);
                else break; // No valid step
                if(cur>end) break; 
                push(cur); 
                iterations++;
            }
        }
    }
    // Include invoices - only "Offen" (open) invoices for future cashflow
    if(q.exec("SELECT status,due_date,amount,paid_date,paid_amount FROM invoices")){
        while(q.next()){
            QString status = q.value(0).toString();
            
            if(status == "Offen" || status == "Überfällig" || status.isEmpty()){ 
                // Open invoice - expect payment on due date (considering partial payments)
                if(includeUnpaidInvoices){
                    QString due_s = q.value(1).toString();
                    QDate due = QDate::fromString(due_s, Qt::ISODate);
                    if(!due.isValid()) {
                        due = QDate::fromString(due_s, "dd.MM.yyyy");
                    }
                    if(!due.isValid()) {
                        due = QDate::fromString(due_s, "yyyy-MM-dd");
                    }
                    double totalAmt=q.value(2).toDouble();
                    double paidAmt = q.value(4).isNull() ? 0.0 : q.value(4).toDouble();
                    double remainingAmt = totalAmt - paidAmt;
                    qDebug() << "Invoice date string:" << due_s << "Parsed as:" << due << "Total:" << totalAmt << "Paid:" << paidAmt << "Remaining:" << remainingAmt << "Status:" << status;
                    if(due.isValid() && remainingAmt != 0) {
                        evs.push_back({due,remainingAmt}); 
                        qDebug() << "Added open invoice:" << due << remainingAmt << "Status:" << status;
                    } else {
                        qDebug() << "SKIPPED invoice - invalid date or zero remaining amount";
                    }
                }
            }
            else if(status == "Bezahlt"){
                // Paid invoice - use actual payment date (for historical data)
                QString paid_s=q.value(3).toString(); 
                if(!paid_s.isEmpty()){
                    QDate paidD=QDate::fromString(paid_s,Qt::ISODate); 
                    double amt=q.value(4).isNull()?q.value(2).toDouble():q.value(4).toDouble(); 
                    if(paidD.isValid() && amt != 0) {
                        evs.push_back({paidD,amt}); 
                        qDebug() << "Added paid invoice:" << paidD << amt;
                    }
                }
            }
            // Storniert (cancelled) invoices are ignored
        }
    }
    if(includeOffersOffen || includeOffersBeauftragt){
        QString statusCondition;
        if(includeOffersOffen && includeOffersBeauftragt){
            statusCondition = "WHERE status = 'Offen' OR status = 'Beauftragt' OR status IS NULL";
        } else if(includeOffersOffen){
            statusCondition = "WHERE status = 'Offen' OR status IS NULL";
        } else if(includeOffersBeauftragt){
            statusCondition = "WHERE status = 'Beauftragt'";
        }
        
        QString query = QString("SELECT date_expected,amount,probability,payment_delay,status FROM offers %1").arg(statusCondition);
        qDebug() << "Offers query:" << query;
        if(q.exec(query)){
            qDebug() << "Offers query executed successfully";
            while(q.next()){
                QString date_s = q.value(0).toString();
                QDate de = QDate::fromString(date_s, Qt::ISODate);
                if(!de.isValid()) {
                    de = QDate::fromString(date_s, "dd.MM.yyyy");
                }
                
                double amt=q.value(1).toDouble(); 
                double p=q.value(2).toDouble(); 
                int paymentDelay = q.value(3).toInt();
                QString status = q.value(4).toString();
                
                qDebug() << "Offer found - Date:" << date_s << "Parsed:" << de << "Amount:" << amt << "Probability:" << p << "Status:" << status;
                
                if(p>1.0) p/=100.0; 
                
                // Include full amount if probability > 0%
                if(p > 0 && de.isValid()) {
                    // Apply payment delay to the expected date
                    QDate paymentDate = de.addDays(paymentDelay);
                    
                    if(paymentDate.isValid()) {
                        evs.push_back({paymentDate, amt}); // Use full amount, not weighted
                        qDebug() << "Added offer to cashflow:" << de << "->" << paymentDate << "(+" << paymentDelay << "days)" << amt;
                    } else {
                        qDebug() << "Invalid payment date for offer after adding delay";
                    }
                } else if(p <= 0) {
                    qDebug() << "Offer skipped due to 0% probability";
                } else {
                    qDebug() << "Offer skipped due to invalid date";
                }
            }
        }
    }
    // Sort events by date
    std::sort(evs.begin(), evs.end(), [](const Ev&a,const Ev&b){return a.d<b.d;});
    
    // Create maps for all months from current month to horizon
    QMap<QString,double> netMap;
    QMap<QString,double> incomeMap;
    QMap<QString,double> expensesMap;
    QDate currentDate = QDate::currentDate();
    QDate startOfMonth = QDate(currentDate.year(), currentDate.month(), 1);
    
    // Initialize all months in the horizon with 0
    for(int i = 0; i < horizonMonths; ++i) {
        QDate monthDate = addMonthsClamped(startOfMonth, i);
        QString label = monthLabel(monthDate.year(), monthDate.month());
        netMap[label] = 0.0;
        incomeMap[label] = 0.0;
        expensesMap[label] = 0.0;
    }
    
    // Add events to the appropriate months (only current and future)
    for(const auto&e: evs){ 
        if(!e.d.isValid()) continue;
        // Only include events from current month onwards
        if(e.d >= startOfMonth) {
            QString label = monthLabel(e.d.year(),e.d.month());
            // Only add if within our horizon
            if(netMap.contains(label)) {
                netMap[label] += e.a;
                if(e.a > 0) {
                    incomeMap[label] += e.a;
                } else {
                    expensesMap[label] += e.a;  // Keep negative
                }
            }
        }
    }
    
    QVector<MonthRow> out; 
    out.reserve(netMap.size()); 
    for(auto it=netMap.begin(); it!=netMap.end(); ++it){ 
        out.push_back({it.key(), it.value(), incomeMap[it.key()], expensesMap[it.key()]}); 
    }
    return out;
}

double Database::activeOffersSum(){
    QSqlQuery q(m_db);
    double sum = 0.0;
    if(q.exec("SELECT amount, probability FROM offers WHERE status = 'Offen' OR status = 'Beauftragt' OR status IS NULL")){
        while(q.next()){
            double amt = q.value(0).toDouble();
            double prob = q.value(1).toDouble();
            if(prob > 1.0) prob /= 100.0;
            // Include full amount if probability > 0%
            if(prob > 0) {
                sum += amt; // Use full amount, not weighted
            }
        }
    }
    return sum;
}

double Database::openInvoicesSum(){
    QSqlQuery q(m_db);
    double sum = 0.0;
    if(q.exec("SELECT amount, paid_amount FROM invoices WHERE status = 'Offen' OR status = 'Überfällig' OR status IS NULL")){
        while(q.next()){
            double totalAmount = q.value(0).toDouble();
            double paidAmount = q.value(1).isNull() ? 0.0 : q.value(1).toDouble();
            double remainingAmount = totalAmount - paidAmount;
            sum += remainingAmount;
        }
    }
    return sum;
}

QString Database::nextOfferNumber(){
    QSqlQuery q(m_db);
    QString prefix = QString("ANG-%1-").arg(QDate::currentDate().year());
    
    // Find the highest number for the current year
    QString query = QString("SELECT offer_number FROM offers WHERE offer_number LIKE '%1%' ORDER BY offer_number DESC LIMIT 1").arg(prefix);
    if(q.exec(query) && q.next()){
        QString lastNumber = q.value(0).toString();
        // Extract the number part after the prefix
        QString numberPart = lastNumber.mid(prefix.length());
        int num = numberPart.toInt() + 1;
        return prefix + QString("%1").arg(num, 4, 10, QChar('0'));
    }
    // First offer of the year
    return prefix + "0001";
}
