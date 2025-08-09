#include "TargetsPage.h"
#include "Database.h"
#include "Dashboard.h"
#include <QSqlTableModel>
#include <QTableView>
#include <QHeaderView>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QPushButton>
#include <QLabel>
#include <QDate>
#include <QMessageBox>
TargetsPage::TargetsPage(QWidget*parent,Dashboard*d):QWidget(parent),m_dashboard(d){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("targets"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange); 
    m_model->select();
    
    // Set German column headers
    m_model->setHeaderData(1, Qt::Horizontal, "Jahr");
    m_model->setHeaderData(2, Qt::Horizontal, "Monat");
    m_model->setHeaderData(3, Qt::Horizontal, "Zielbetrag (€)");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Hide id column
    m_view->hideColumn(0);
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    
    m_add=new QPushButton("➕ Neue Fixkosten"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    // Add info label
    auto*infoLabel = new QLabel("💡 <b>Tipp:</b> Tragen Sie hier Ihre monatlichen Fixkosten ein (Miete, Versicherungen, Abos, etc.)");
    infoLabel->setStyleSheet("QLabel { background-color: #f0f8ff; padding: 8px; border-radius: 5px; }");
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addWidget(infoLabel);
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&TargetsPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&TargetsPage::removeRow);
}
void TargetsPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("year")), QDate::currentDate().year()); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("month")), QDate::currentDate().month()); 
    m_view->selectRow(row); 
}
void TargetsPage::removeRow(){ 
    auto idx=m_view->currentIndex(); 
    if(!idx.isValid()) {
        QMessageBox::warning(this, "Keine Auswahl", "Bitte wählen Sie eine Zeile zum Löschen aus.");
        return;
    }
    
    int ret = QMessageBox::question(this, "Löschen bestätigen", 
                                   "Möchten Sie diese Fixkosten wirklich löschen?",
                                   QMessageBox::Yes | QMessageBox::No);
    if(ret == QMessageBox::Yes) {
        m_model->removeRow(idx.row());
        m_model->submitAll();
        m_model->select();
    }
}
