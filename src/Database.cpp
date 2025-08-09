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
    if(q.lastError().isValid()){ qWarning()<<"Schema error:"<<q.lastError().text(); }
}

double Database::settingStartBalance(){ QSqlQuery q(m_db); q.prepare("SELECT value FROM settings WHERE key='start_balance'"); if(q.exec()&&q.next()) return q.value(0).toDouble(); return 0.0; }
void Database::setSettingStartBalance(double v){ QSqlQuery q(m_db); q.prepare("INSERT INTO settings(key,value) VALUES('start_balance',:v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;"); q.bindValue(":v",v); q.exec(); }

QMap<QString,double> Database::targets(){ QMap<QString,double> out; QSqlQuery q(m_db); if(q.exec("SELECT year,month,amount FROM targets")){ while(q.next()){ out[monthLabel(q.value(0).toInt(), q.value(1).toInt())]=q.value(2).toDouble(); } } return out; }

QVector<MonthRow> Database::monthlyCashflow(int horizonMonths,bool includeOffers,bool includeUnpaidInvoices,bool includeRecurring){
    struct Ev{QDate d; double a;}; QList<Ev> evs;
    QSqlQuery q(m_db);
    q.exec("SELECT date,amount,COALESCE(interval,'') FROM transactions");
    while(q.next()){
        QDate d=QDate::fromString(q.value(0).toString(),Qt::ISODate);
        double a=q.value(1).toDouble();
        QString it=q.value(2).toString().toLower().trimmed();
        if(!includeRecurring || it.isEmpty()||it=="once"||it=="einmalig"){ evs.push_back({d,a}); }
        else{
            QDate cur=d; auto push=[&](const QDate&dd){ evs.push_back({dd,a}); };
            push(cur);
            int stepM = (it=="monthly")?1:(it=="quarterly")?3:(it=="semiannual"||it=="semi-annually"||it=="halbjahr"||it=="halbjährlich")?6:(it=="yearly")?12:0;
            int stepD = (it=="biweekly")?14:(it=="weekly")?7:0;
            QDate end=addMonthsClamped(QDate::currentDate(),horizonMonths); end=QDate(end.year(),end.month(),QDate(end.year(),end.month(),1).daysInMonth());
            while(true){ if(stepM>0) cur=addMonthsClamped(cur,stepM); else cur=cur.addDays(stepD); if(cur>end) break; push(cur); }
        }
    }
    if(includeUnpaidInvoices || true){
        if(q.exec("SELECT paid_date,paid_amount,due_date,amount FROM invoices")){
            while(q.next()){
                QString paid_s=q.value(0).toString(); bool paid=!paid_s.isEmpty();
                if(paid){ QDate paidD=QDate::fromString(paid_s,Qt::ISODate); double amt=q.value(1).isNull()?q.value(3).toDouble():q.value(1).toDouble(); evs.push_back({paidD,amt}); }
                else if(includeUnpaidInvoices){ QDate due=QDate::fromString(q.value(2).toString(),Qt::ISODate); double amt=q.value(3).toDouble(); if(due.isValid()) evs.push_back({due,amt}); }
            }
        }
    }
    if(includeOffers){
        if(q.exec("SELECT date_expected,amount,probability FROM offers")){
            while(q.next()){
                QDate de=QDate::fromString(q.value(0).toString(),Qt::ISODate);
                double amt=q.value(1).toDouble(); double p=q.value(2).toDouble(); if(p>1.0) p/=100.0; if(p<0)p=0; if(p>1)p=1;
                if(de.isValid()) evs.push_back({de, amt*p});
            }
        }
    }
    if(evs.isEmpty()) return {};
    std::sort(evs.begin(), evs.end(), [](const Ev&a,const Ev&b){return a.d<b.d;});
    QMap<QString,double> m; for(const auto&e: evs){ if(!e.d.isValid()) continue; m[monthLabel(e.d.year(),e.d.month())]+=e.a; }
    QVector<MonthRow> out; out.reserve(m.size()); for(auto it=m.begin(); it!=m.end(); ++it){ out.push_back({it.key(), it.value()}); }
    return out;
}
