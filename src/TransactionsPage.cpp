#include "TransactionsPage.h"
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
TransactionsPage::TransactionsPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("transactions"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange);  // Auto-save changes
    m_model->select();
    
    // Set better column headers
    m_model->setHeaderData(1, Qt::Horizontal, "Datum");
    m_model->setHeaderData(2, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(7, Qt::Horizontal, "Intervall");
    m_model->setHeaderData(8, Qt::Horizontal, "Notizen");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(8, QHeaderView::Interactive); // Notes column can be resized
    
    // Hide unnecessary columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(4);  // category_id
    m_view->hideColumn(5);  // person_id
    m_view->hideColumn(9);  // created_at
    m_view->hideColumn(10); // updated_at
    
    m_add=new QPushButton("➕ Neue Transaktion"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    // Add info label
    auto*infoLabel = new QLabel("💡 <b>Tipp:</b> Positive Beträge = Einnahmen, Negative Beträge = Ausgaben");
    infoLabel->setStyleSheet("QLabel { background-color: #f0f8ff; padding: 8px; border-radius: 5px; }");
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addWidget(infoLabel);
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&TransactionsPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&TransactionsPage::removeRow);
}
void TransactionsPage::addRow(){ int row=m_model->rowCount(); m_model->insertRow(row); m_model->setData(m_model->index(row,m_model->fieldIndex("date")), QDate::currentDate().toString(Qt::ISODate)); m_view->selectRow(row); }
void TransactionsPage::removeRow(){ 
    auto idx=m_view->currentIndex(); 
    if(!idx.isValid()) {
        QMessageBox::warning(this, "Keine Auswahl", "Bitte wählen Sie eine Zeile zum Löschen aus.");
        return;
    }
    
    int ret = QMessageBox::question(this, "Löschen bestätigen", 
                                   "Möchten Sie diese Transaktion wirklich löschen?",
                                   QMessageBox::Yes | QMessageBox::No);
    if(ret == QMessageBox::Yes) {
        m_model->removeRow(idx.row());
        m_model->submitAll();  // Save deletion immediately
        m_model->select();      // Refresh view
    }
}
