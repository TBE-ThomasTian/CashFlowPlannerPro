#include "OffersPage.h"
#include "Database.h"
#include "TableDelegates.h"
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

class StatusDelegate : public QStyledItemDelegate {
public:
    StatusDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        auto *editor = new QComboBox(parent);
        editor->addItem("Offen");
        editor->addItem("Beauftragt");
        editor->addItem("Abgelehnt");
        return editor;
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        auto *comboBox = static_cast<QComboBox*>(editor);
        QString value = index.model()->data(index, Qt::EditRole).toString();
        comboBox->setCurrentText(value);
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        auto *comboBox = static_cast<QComboBox*>(editor);
        model->setData(index, comboBox->currentText(), Qt::EditRole);
    }
};

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
    
    // ACTUAL column order from database:
    // 0: id
    // 1: date_expected  
    // 2: customer
    // 3: amount
    // 4: probability
    // 5: description
    // 6: status
    // 7: payment_delay
    // 8: created_at
    // 9: offer_number
    // 10: offer_date
    
    // Set correct German headers matching ACTUAL positions
    m_model->setHeaderData(1, Qt::Horizontal, "Erwartetes Datum");
    m_model->setHeaderData(2, Qt::Horizontal, "Kunde");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(4, Qt::Horizontal, "Wahrscheinlichkeit (%)");
    m_model->setHeaderData(5, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(6, Qt::Horizontal, "Status");
    m_model->setHeaderData(7, Qt::Horizontal, "Zahlungsziel");
    m_model->setHeaderData(9, Qt::Horizontal, "Angebotsnummer");
    m_model->setHeaderData(10, Qt::Horizontal, "Angebotsdatum");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegates based on ACTUAL column positions
    m_view->setItemDelegateForColumn(3, new CurrencyDelegate(this));  // Amount column (actual position 3)
    m_view->setItemDelegateForColumn(4, new PercentDelegate(this));   // Probability column (actual position 4)
    m_view->setItemDelegateForColumn(6, new StatusDelegate(this));  // Status column (actual position 6)
    m_view->setItemDelegateForColumn(7, new PaymentDelayDelegate(this));  // Payment delay column (actual position 7)
    
    // Hide id and timestamp columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(8);  // created_at
    
    // Reorder visual columns to show offer_number and offer_date at the beginning
    // We need to move columns to get this order:
    // offer_number(9), offer_date(10), date_expected(1), customer(2), amount(3), probability(4), status(6), description(5), payment_delay(7)
    QHeaderView* header = m_view->horizontalHeader();
    
    // First, move offer_number from logical position 9 to visual position 1 (after hidden id)
    header->moveSection(header->visualIndex(9), 1);
    // Then move offer_date from logical position 10 to visual position 2
    header->moveSection(header->visualIndex(10), 2);
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(1, QHeaderView::ResizeToContents); // Date expected
    m_view->horizontalHeader()->setSectionResizeMode(5, QHeaderView::Interactive); // Description column
    m_view->horizontalHeader()->setSectionResizeMode(9, QHeaderView::ResizeToContents); // Offer number
    m_view->horizontalHeader()->setSectionResizeMode(10, QHeaderView::ResizeToContents); // Offer date
    
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
    m_model->setData(m_model->index(row,m_model->fieldIndex("offer_number")), Database::instance().nextOfferNumber());
    m_model->setData(m_model->index(row,m_model->fieldIndex("offer_date")), QDate::currentDate().toString(Qt::ISODate));
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