#include "TaxesPage.h"
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

class TaxIntervalDelegate : public QStyledItemDelegate {
public:
    TaxIntervalDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        if (index.column() == 4) {  // interval column
            auto *editor = new QComboBox(parent);
            editor->addItems({"Monatlich", "Vierteljährlich", "Jährlich"});
            return editor;
        }
        return QStyledItemDelegate::createEditor(parent, option, index);
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        if (index.column() == 4) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            QString value = index.model()->data(index, Qt::EditRole).toString();
            comboBox->setCurrentText(value);
        } else {
            QStyledItemDelegate::setEditorData(editor, index);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        if (index.column() == 4) {
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
        if (index.column() == 2) {  // tax type column
            auto *editor = new QComboBox(parent);
            editor->addItems({"Umsatzsteuer", "Lohnsteuer", "Gewerbesteuer", "Kapitalertragsteuer", "Einkommensteuer", "Körperschaftsteuer"});
            return editor;
        }
        return QStyledItemDelegate::createEditor(parent, option, index);
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        if (index.column() == 2) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            QString value = index.model()->data(index, Qt::EditRole).toString();
            comboBox->setCurrentText(value);
        } else {
            QStyledItemDelegate::setEditorData(editor, index);
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        if (index.column() == 2) {
            auto *comboBox = static_cast<QComboBox*>(editor);
            model->setData(index, comboBox->currentText(), Qt::EditRole);
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
    
    // Set German column headers
    m_model->setHeaderData(1, Qt::Horizontal, "Fällig am");
    m_model->setHeaderData(2, Qt::Horizontal, "Steuerart");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(4, Qt::Horizontal, "Intervall");
    m_model->setHeaderData(8, Qt::Horizontal, "Bemerkungen");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegates for dropdowns
    m_view->setItemDelegateForColumn(2, new TaxTypeDelegate(this));
    m_view->setItemDelegateForColumn(4, new TaxIntervalDelegate(this));
    
    // Hide unnecessary columns
    m_view->hideColumn(0);  // id
    m_view->hideColumn(5);  // category_id
    m_view->hideColumn(6);  // person_id
    m_view->hideColumn(7);  // interval (we use column 4 for display)
    m_view->hideColumn(9);  // created_at
    m_view->hideColumn(10); // updated_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    m_view->horizontalHeader()->setSectionResizeMode(8, QHeaderView::Interactive); // Notes column can be resized
    
    m_add=new QPushButton("➕ Neue Steuer"); 
    m_del=new QPushButton("🗑️ Löschen");
    
    // Add info labels
    auto*infoLabel = new QLabel("💡 <b>Tipp:</b> Tragen Sie hier Ihre regelmäßigen Steuerzahlungen ein:");
    infoLabel->setStyleSheet("QLabel { background-color: #f0f8ff; padding: 8px; border-radius: 5px; }");
    
    auto*exampleLabel = new QLabel("📌 <b>Beispiele:</b> Umsatzsteuer (monatlich), Lohnsteuer (monatlich), "
                                   "Gewerbesteuer-Vorauszahlung (vierteljährlich), Einkommensteuer-Vorauszahlung (vierteljährlich)");
    exampleLabel->setWordWrap(true);
    exampleLabel->setStyleSheet("QLabel { background-color: #fffaf0; padding: 8px; border-radius: 5px; }");
    
    auto*btns=new QHBoxLayout(); 
    btns->addWidget(m_add); 
    btns->addWidget(m_del); 
    btns->addStretch(1);
    
    auto*lay=new QVBoxLayout(this); 
    lay->addWidget(infoLabel);
    lay->addWidget(exampleLabel);
    lay->addLayout(btns); 
    lay->addWidget(m_view,1);
    
    connect(m_add,&QPushButton::clicked,this,&TaxesPage::addRow);
    connect(m_del,&QPushButton::clicked,this,&TaxesPage::removeRow);
}

void TaxesPage::addRow(){ 
    int row=m_model->rowCount(); 
    m_model->insertRow(row); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("date")), QDate::currentDate().toString(Qt::ISODate)); 
    m_model->setData(m_model->index(row,m_model->fieldIndex("description")), "Umsatzsteuer");
    m_model->setData(m_model->index(row,m_model->fieldIndex("notes")), "STEUER:Monatlich");
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

