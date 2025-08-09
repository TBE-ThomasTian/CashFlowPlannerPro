#pragma once
#include <QWidget>
class QTabWidget; class Dashboard; class TransactionsPage; class OffersPage; class InvoicesPage; class TargetsPage; class TaxesPage;
class MainWindow: public QWidget{
    Q_OBJECT
public:
    explicit MainWindow(QWidget*parent=nullptr);
private:
    QTabWidget*m_tabs;
    Dashboard*m_dashboard;
    TransactionsPage*m_txPage;
    OffersPage*m_offersPage;
    InvoicesPage*m_invoicesPage;
    TargetsPage*m_targetsPage;
    TaxesPage*m_taxesPage;
};
