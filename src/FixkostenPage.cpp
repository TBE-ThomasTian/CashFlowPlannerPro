#include "FixkostenPage.h"
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

class FixkostenIntervalDelegate : public QStyledItemDelegate {
public:
    FixkostenIntervalDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        if (index.column() == 6) {  // interval column
            auto *editor = new QComboBox(parent);
            editor->addItems({"", "Monatlich", "Vierteljährlich", "Jährlich"});
            return editor;
        }
        return QStyledItemDelegate::createEditor(parent, option, index);
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        if (index.column() == 6) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            QString value = index.model()->data(index, Qt::EditRole).toString();
            comboBox->setCurrentText(value);
        } else {
            QStyledItemDelegate::setEditorData(editor, index);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        if (index.column() == 6) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            model->setData(index, comboBox->currentText(), Qt::EditRole);
        } else {
            QStyledItemDelegate::setModelData(editor, model, index);
        }
    }
};

FixkostenPage::FixkostenPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("transactions"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange);
    m_model->setFilter("notes LIKE 'FIXKOSTEN:%'");
    m_model->select();
    
    // Set German column headers - exactly like TransactionsPage
    m_model->setHeaderData(1, Qt::Horizontal, "Datum");
    m_model->setHeaderData(2, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(6, Qt::Horizontal, "Intervall");
    m_model->setHeaderData(7, Qt::Horizontal, "Notizen");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegate for interval dropdown
    m_view->setItemDelegateForColumn(6, new FixkostenIntervalDelegate(this));
    
    // Hide unnecessary columns - exactly like TransactionsPage
    m_view->hideColumn(0);  // id
    m_view->hideColumn(4);  // category_id
    m_view->hideColumn(5);  // person_id
    m_view->hideColumn(8);  // created_at
    m_view->hideColumn(9);  // updated_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(7, QHeaderView::Interactive); // Notes column can be resized
    
    m_add=new QPushButton("➕ Neue Fixkosten"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&FixkostenPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&FixkostenPage::removeRow);
}

void FixkostenPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("date")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("description")), "Neue Fixkosten");
    m_model->setData(m_model->index(row,m_model->fieldIndex("notes")), "FIXKOSTEN:");
    m_model->setData(m_model->index(row,m_model->fieldIndex("interval")), "Monatlich");
    m_model->setData(m_model->index(row,m_model->fieldIndex("amount")), -100.00);
    m_view->selectRow(row); 
}

void FixkostenPage::removeRow(){ 
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