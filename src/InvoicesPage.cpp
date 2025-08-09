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
#include <QComboBox>
#include <QStyledItemDelegate>

class InvoiceStatusDelegate : public QStyledItemDelegate {
public:
    InvoiceStatusDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        if (index.column() == 8) {  // status column
            auto *editor = new QComboBox(parent);
            editor->addItems({"Offen", "Bezahlt", "Überfällig", "Storniert"});
            return editor;
        }
        return QStyledItemDelegate::createEditor(parent, option, index);
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        if (index.column() == 8) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            QString value = index.model()->data(index, Qt::EditRole).toString();
            comboBox->setCurrentText(value);
        } else {
            QStyledItemDelegate::setEditorData(editor, index);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        if (index.column() == 8) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            QString status = comboBox->currentText();
            model->setData(index, status, Qt::EditRole);
            
            // If status is "Bezahlt", set paid_date to today
            if (status == "Bezahlt") {
                int row = index.row();
                model->setData(model->index(row, 6), QDate::currentDate().toString(Qt::ISODate), Qt::EditRole);
                // Set paid_amount = amount if not already set
                QVariant amount = model->data(model->index(row, 4), Qt::EditRole);
                QVariant paidAmount = model->data(model->index(row, 7), Qt::EditRole);
                if (paidAmount.isNull() || paidAmount.toDouble() == 0) {
                    model->setData(model->index(row, 7), amount, Qt::EditRole);
                }
            } else {
                // Clear paid_date if not paid
                int row = index.row();
                model->setData(model->index(row, 6), QVariant(), Qt::EditRole);
            }
        } else {
            QStyledItemDelegate::setModelData(editor, model, index);
        }
    }
};

InvoicesPage::InvoicesPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("invoices"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange); 
    m_model->select();
    
    // Set German column headers
    m_model->setHeaderData(1, Qt::Horizontal, "Ausstellungsdatum");
    m_model->setHeaderData(2, Qt::Horizontal, "Fällig am");
    m_model->setHeaderData(3, Qt::Horizontal, "Kunde");
    m_model->setHeaderData(4, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(5, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(6, Qt::Horizontal, "Bezahlt am");
    m_model->setHeaderData(7, Qt::Horizontal, "Bezahlter Betrag");
    m_model->setHeaderData(8, Qt::Horizontal, "Status");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegate for status dropdown
    m_view->setItemDelegateForColumn(8, new InvoiceStatusDelegate(this));
    
    // Hide id and timestamp columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(9);  // created_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(5, QHeaderView::Interactive); // Description column can be resized
    
    m_add=new QPushButton("➕ Neue Rechnung"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&InvoicesPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&InvoicesPage::removeRow);
}

void InvoicesPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("issue_date")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("due_date")), QDate::currentDate().addDays(30).toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("status")), "Offen");
    m_model->setData(m_model->index(row,m_model->fieldIndex("amount")), 0.00);
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