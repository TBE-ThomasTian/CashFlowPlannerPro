#include "TaxesPage.h"
#include "Database.h"
#include "TableDelegates.h"
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

class TaxIntervalDelegate : public QStyledItemDelegate {
public:
    TaxIntervalDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        if (index.column() == 6) {  // interval column
            auto *editor = new QComboBox(parent);
            editor->addItems({"Einmalig", "Monatlich", "Vierteljährlich", "Jährlich"});
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

class TaxTypeDelegate : public QStyledItemDelegate {
public:
    TaxTypeDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        if (index.column() == 7) {  // notes column used for tax type
            auto *editor = new QComboBox(parent);
            editor->addItems({"Umsatzsteuer", "Gewerbesteuer", "Kapitalertragsteuer"});
            return editor;
        }
        return QStyledItemDelegate::createEditor(parent, option, index);
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        if (index.column() == 7) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            QString value = index.model()->data(index, Qt::EditRole).toString();
            // Extract type from "STEUER:Type" format
            if (value.startsWith("STEUER:")) {
                value = value.mid(7);
            }
            comboBox->setCurrentText(value);
        } else {
            QStyledItemDelegate::setEditorData(editor, index);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        if (index.column() == 7) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            model->setData(index, "STEUER:" + comboBox->currentText(), Qt::EditRole);
        } else {
            QStyledItemDelegate::setModelData(editor, model, index);
        }
    }
};

TaxesPage::TaxesPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("transactions"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange);
    m_model->setFilter("notes LIKE 'STEUER:%'");
    m_model->select();
    
    // Set German column headers - hide notes column since it's auto-managed
    m_model->setHeaderData(1, Qt::Horizontal, "Fälligkeitsdatum");
    m_model->setHeaderData(2, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(6, Qt::Horizontal, "Intervall");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegates
    m_view->setItemDelegateForColumn(3, new CurrencyDelegate(this));  // Amount column
    m_view->setItemDelegateForColumn(6, new TaxIntervalDelegate(this));
    
    // Hide unnecessary columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(4);  // category_id
    m_view->hideColumn(5);  // person_id
    m_view->hideColumn(7);  // notes (auto-managed with STEUER: prefix)
    m_view->hideColumn(8);  // created_at
    m_view->hideColumn(9);  // updated_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    // All columns stretch evenly since notes column is hidden
    
    m_add=new QPushButton("➕ Neue Steuerzahlung"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&TaxesPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&TaxesPage::removeRow);
}

void TaxesPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("date")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("description")), "Steuervorauszahlung");
    m_model->setData(m_model->index(row,m_model->fieldIndex("notes")), "STEUER:Umsatzsteuer");
    m_model->setData(m_model->index(row,m_model->fieldIndex("interval")), "Monatlich");
    m_model->setData(m_model->index(row,m_model->fieldIndex("amount")), -1000.00);
    m_view->selectRow(row); 
}

void TaxesPage::removeRow(){ 
    auto idx=m_view->currentIndex(); 
    if(!idx.isValid()) {
        QMessageBox::warning(this, "Keine Auswahl", "Bitte wählen Sie eine Zeile zum Löschen aus.");
        return;
    }
    
    int ret = QMessageBox::question(this, "Löschen bestätigen", 
                                   "Möchten Sie diese Steuerzahlung wirklich löschen?",
                                   QMessageBox::Yes | QMessageBox::No);
    if(ret == QMessageBox::Yes) {
        m_model->removeRow(idx.row());
        m_model->submitAll();  
        m_model->select();      
    }
}