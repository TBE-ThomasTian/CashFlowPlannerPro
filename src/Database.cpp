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
        date_expected TEXT,
        customer TEXT,
        amount REAL,
        probability REAL,
        description TEXT,
        status TEXT,
        payment_delay INTEGER DEFAULT 30,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
    );)");
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
    
    if(q.lastError().isValid() && !q.lastError().text().contains("duplicate column")){ 
        qWarning()<<"Schema error:"<<q.lastError().text(); 
    }
}

double Database::settingStartBalance(){ QSqlQuery q(m_db); q.prepare("SELECT value FROM settings WHERE key='start_balance'"); if(q.exec()&&q.next()) return q.value(0).toDouble(); return 0.0; }
void Database::setSettingStartBalance(double v){ QSqlQuery q(m_db); q.prepare("INSERT INTO settings(key,value) VALUES('start_balance',:v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;"); q.bindValue(":v",v); q.exec(); }

QMap<QString,double> Database::targets(){ QMap<QString,double> out; QSqlQuery q(m_db); if(q.exec("SELECT year,month,amount FROM targets")){ while(q.next()){ out[monthLabel(q.value(0).toInt(), q.value(1).toInt())]=q.value(2).toDouble(); } } return out; }

QVector<MonthRow> Database::monthlyCashflow(int horizonMonths,bool includeOffers,bool includeUnpaidInvoices,bool includeRecurring){
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
                // Open invoice - expect payment on due date
                if(includeUnpaidInvoices){
                    QString due_s = q.value(1).toString();
                    QDate due = QDate::fromString(due_s, Qt::ISODate);
                    if(!due.isValid()) {
                        due = QDate::fromString(due_s, "dd.MM.yyyy");
                    }
                    if(!due.isValid()) {
                        due = QDate::fromString(due_s, "yyyy-MM-dd");
                    }
                    double amt=q.value(2).toDouble(); 
                    qDebug() << "Invoice date string:" << due_s << "Parsed as:" << due << "Amount:" << amt << "Status:" << status;
                    if(due.isValid() && amt != 0) {
                        evs.push_back({due,amt}); 
                        qDebug() << "Added open invoice:" << due << amt << "Status:" << status;
                    } else {
                        qDebug() << "SKIPPED invoice - invalid date or zero amount";
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
    if(includeOffers){
        if(q.exec("SELECT date_expected,amount,probability,payment_delay FROM offers")){
            while(q.next()){
                QDate de=QDate::fromString(q.value(0).toString(),Qt::ISODate);
                double amt=q.value(1).toDouble(); 
                double p=q.value(2).toDouble(); 
                int paymentDelay = q.value(3).toInt();
                
                if(p>1.0) p/=100.0; 
                if(p<0)p=0; 
                if(p>1)p=1;
                
                // Apply payment delay to the expected date
                QDate paymentDate = de.addDays(paymentDelay);
                
                if(paymentDate.isValid()) {
                    evs.push_back({paymentDate, amt*p});
                    qDebug() << "Offer payment:" << de << "->" << paymentDate << "(+" << paymentDelay << "days)" << amt*p;
                }
            }
        }
    }
    if(evs.isEmpty()) return {};
    std::sort(evs.begin(), evs.end(), [](const Ev&a,const Ev&b){return a.d<b.d;});
    QMap<QString,double> m; for(const auto&e: evs){ if(!e.d.isValid()) continue; m[monthLabel(e.d.year(),e.d.month())]+=e.a; }
    QVector<MonthRow> out; out.reserve(m.size()); for(auto it=m.begin(); it!=m.end(); ++it){ out.push_back({it.key(), it.value()}); }
    return out;
}
