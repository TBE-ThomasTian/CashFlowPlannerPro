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
#include <QComboBox>
#include <QStyledItemDelegate>

class IntervalDelegate : public QStyledItemDelegate {
public:
    IntervalDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        auto *editor = new QComboBox(parent);
        editor->addItem("einmalig");
        editor->addItem("täglich");
        editor->addItem("wöchentlich");
        editor->addItem("monatlich");
        editor->addItem("vierteljährlich");
        editor->addItem("halbjährlich");
        editor->addItem("jährlich");
        editor->setEditable(true);  // Allow custom intervals
        return editor;
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        auto *comboBox = static_cast<QComboBox*>(editor);
        QString value = index.model()->data(index, Qt::EditRole).toString();
        
        // Find the item or set as editable text
        int itemIndex = comboBox->findText(value);
        if (itemIndex >= 0) {
            comboBox->setCurrentIndex(itemIndex);
        } else {
            comboBox->setEditText(value);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        auto *comboBox = static_cast<QComboBox*>(editor);
        model->setData(index, comboBox->currentText(), Qt::EditRole);
    }
};

TransactionsPage::TransactionsPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("transactions"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange);  // Auto-save changes
    // Exclude FIXKOSTEN and STEUER entries - they have their own tabs
    m_model->setFilter("(notes NOT LIKE 'FIXKOSTEN:%' AND notes NOT LIKE 'STEUER:%') OR notes IS NULL");
    m_model->select();
    
    // Set better column headers
    m_model->setHeaderData(1, Qt::Horizontal, "Datum");
    m_model->setHeaderData(2, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(6, Qt::Horizontal, "Intervall");
    m_model->setHeaderData(7, Qt::Horizontal, "Notizen");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegate for interval column
    m_view->setItemDelegateForColumn(6, new IntervalDelegate(this));  // Interval column
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(7, QHeaderView::Interactive); // Notes column can be resized
    
    // Hide unnecessary columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(4);  // category_id
    m_view->hideColumn(5);  // person_id
    m_view->hideColumn(8);  // created_at
    m_view->hideColumn(9);  // updated_at
    
    m_add=new QPushButton("➕ Neue Transaktion"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&TransactionsPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&TransactionsPage::removeRow);
}
void TransactionsPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("date")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("interval")), "einmalig");
    m_model->setData(m_model->index(row,m_model->fieldIndex("amount")), 0.00);
    m_view->selectRow(row); 
}
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
