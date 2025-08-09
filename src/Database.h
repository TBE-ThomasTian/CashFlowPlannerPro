#pragma once
#include <QString>
#include <QSqlDatabase>
#include <QVector>
#include <QPair>
#include <QMap>
struct MonthRow{QString ym; double net=0;};
class Database{
public: 
    static Database& instance(); 
    bool open(const QString&path); 
    void close();
    void ensureSchema(); 
    QSqlDatabase db(); 
    double settingStartBalance(); 
    void setSettingStartBalance(double v); 
    QVector<MonthRow> monthlyCashflow(int horizonMonths,bool includeOffers,bool includeUnpaidInvoices,bool includeRecurring); 
    QMap<QString,double> targets(); 
private: 
    Database(); 
    QSqlDatabase m_db;
};
