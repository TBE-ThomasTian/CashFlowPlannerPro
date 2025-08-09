#include "InvoicesPage.h"
#include "Database.h"
#include <QSqlTableModel>
#include <QTableView>
#include <QHeaderView>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QPushButton>
#include <QLabel>
#include <QDate>
#include <QMessageBox>
InvoicesPage::InvoicesPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("invoices"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange); 
    m_model->select();
    
    // Set German column headers
    m_model->setHeaderData(1, Qt::Horizontal, "Rechnungsnr.");
    m_model->setHeaderData(2, Qt::Horizontal, "Kunde");
    m_model->setHeaderData(3, Qt::Horizontal, "Rechnungsdatum");
    m_model->setHeaderData(4, Qt::Horizontal, "Fällig am");
    m_model->setHeaderData(5, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(6, Qt::Horizontal, "Status");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Hide id and timestamp columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(7);  // created_at
    m_view->hideColumn(8);  // updated_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    
    m_add=new QPushButton("➕ Neue Rechnung"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    // Add info label
    auto*infoLabel = new QLabel("💡 <b>Tipp:</b> Verwalten Sie hier Ihre ausgestellten Rechnungen und deren Status");
    infoLabel->setStyleSheet("QLabel { background-color: #f0f8ff; padding: 8px; border-radius: 5px; }");
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addWidget(infoLabel);
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&InvoicesPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&InvoicesPage::removeRow);
}
void InvoicesPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("issue_date")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("status")), "Offen");
    m_view->selectRow(row); 
}
void InvoicesPage::removeRow(){ 
    auto idx=m_view->currentIndex(); 
    if(!idx.isValid()) {
        QMessageBox::warning(this, "Keine Auswahl", "Bitte wählen Sie eine Zeile zum Löschen aus.");
        return;
    }
    
    int ret = QMessageBox::question(this, "Löschen bestätigen", 
                                   "Möchten Sie diese Rechnung wirklich löschen?",
                                   QMessageBox::Yes | QMessageBox::No);
    if(ret == QMessageBox::Yes) {
        m_model->removeRow(idx.row());
        m_model->submitAll();
        m_model->select();
    }
}
