#include "MainWindow.h"
#include "Dashboard.h"
#include "TransactionsPage.h"
#include "OffersPage.h"
#include "InvoicesPage.h"
#include "TargetsPage.h"
#include "TaxesPage.h"
#include "Database.h"
#include <QTabWidget>
#include <QVBoxLayout>
MainWindow::MainWindow(QWidget*parent):QWidget(parent){
    // DISABLE ALL EFFECTS
    
    // Database is now managed by App class
    
    m_tabs=new QTabWidget(this);
    m_tabs->setDocumentMode(true);
    
    m_dashboard=new Dashboard(this);
    m_txPage=new TransactionsPage(this);
    m_offersPage=new OffersPage(this);
    m_invoicesPage=new InvoicesPage(this);
    m_targetsPage=new TargetsPage(this,m_dashboard);
    m_taxesPage=new TaxesPage(this);
    
    auto*lay=new QVBoxLayout(this);
    lay->setContentsMargins(8,8,8,8);
    lay->addWidget(m_tabs);
    
    m_tabs->addTab(m_dashboard,"Übersicht");
    m_tabs->addTab(m_txPage,"Ein/Ausgaben");
    m_tabs->addTab(m_targetsPage,"Fixkosten");  // Changed to fixed costs
    m_tabs->addTab(m_taxesPage,"Steuer");
    m_tabs->addTab(m_invoicesPage,"Rechnungen");
    m_tabs->addTab(m_offersPage,"Angebote");
}
