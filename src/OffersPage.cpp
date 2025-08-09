#include "OffersPage.h"
#include "Database.h"
#include <QSqlTableModel>
#include <QTableView>
#include <QHeaderView>
#include <QAbstractItemView>
#include <QPushButton>
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QLabel>
#include <QDate>
#include <QMessageBox>
#include <QComboBox>
#include <QStyledItemDelegate>

class PaymentDelayDelegate : public QStyledItemDelegate {
public:
    PaymentDelayDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        if (index.column() == 7) {  // payment_delay column
            auto *editor = new QComboBox(parent);
            editor->addItem("Sofort", 0);
            editor->addItem("30 Tage", 30);
            editor->addItem("60 Tage", 60);
            editor->addItem("90 Tage", 90);
            editor->addItem("120 Tage", 120);
            editor->setEditable(true);  // Allow custom values
            return editor;
        }
        return QStyledItemDelegate::createEditor(parent, option, index);
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        if (index.column() == 7) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            int value = index.model()->data(index, Qt::EditRole).toInt();
            
            // Find matching item or set custom value
            int itemIndex = comboBox->findData(value);
            if (itemIndex >= 0) {
                comboBox->setCurrentIndex(itemIndex);
            } else {
                comboBox->setEditText(QString::number(value) + " Tage");
            }
        } else {
            QStyledItemDelegate::setEditorData(editor, index);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        if (index.column() == 7) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            int value = 30; // Default
            
            // Check if user selected preset or entered custom value
            if (comboBox->currentData().isValid()) {
                value = comboBox->currentData().toInt();
            } else {
                QString text = comboBox->currentText();
                value = text.remove(" Tage").toInt();
            }
            
            model->setData(index, value, Qt::EditRole);
        } else {
            QStyledItemDelegate::setModelData(editor, model, index);
        }
    }
    
    QString displayText(const QVariant &value, const QLocale &locale) const override {
        int days = value.toInt();
        if (days == 0) return "Sofort";
        return QString::number(days) + " Tage";
    }
};

OffersPage::OffersPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("offers"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange); 
    m_model->select();
    
    // Set German column headers - reordered for better flow
    m_model->setHeaderData(1, Qt::Horizontal, "Erwartetes Datum");
    m_model->setHeaderData(2, Qt::Horizontal, "Kunde");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(4, Qt::Horizontal, "Wahrscheinlichkeit (%)");
    m_model->setHeaderData(5, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(6, Qt::Horizontal, "Status");
    m_model->setHeaderData(7, Qt::Horizontal, "Zahlungsziel");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegate for payment delay
    m_view->setItemDelegateForColumn(7, new PaymentDelayDelegate(this));
    
    // Hide id and timestamp columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(8);  // created_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(5, QHeaderView::Interactive); // Description column can be resized
    
    m_add=new QPushButton("➕ Neues Angebot"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&OffersPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&OffersPage::removeRow);
}

void OffersPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("date_expected")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("status")), "Offen");
    m_model->setData(m_model->index(row,m_model->fieldIndex("probability")), 50);
    m_model->setData(m_model->index(row,m_model->fieldIndex("payment_delay")), 30);
    m_model->setData(m_model->index(row,m_model->fieldIndex("amount")), 0.00);
    m_view->selectRow(row); 
}

void OffersPage::removeRow(){ 
    auto idx=m_view->currentIndex(); 
    if(!idx.isValid()) {
        QMessageBox::warning(this, "Keine Auswahl", "Bitte wählen Sie eine Zeile zum Löschen aus.");
        return;
    }
    
    int ret = QMessageBox::question(this, "Löschen bestätigen", 
                                   "Möchten Sie dieses Angebot wirklich löschen?",
                                   QMessageBox::Yes | QMessageBox::No);
    if(ret == QMessageBox::Yes) {
        m_model->removeRow(idx.row());
        m_model->submitAll();
        m_model->select();
    }
}