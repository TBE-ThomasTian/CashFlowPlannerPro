#include "FixkostenPage.h"
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
#include <QSqlQuery>

class FixkostenIntervalDelegate : public QStyledItemDelegate {
public:
    FixkostenIntervalDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
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

class CategoryDelegate : public QStyledItemDelegate {
public:
    CategoryDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {
        loadCategories();
    }
    
    void loadCategories() {
        m_categories.clear();
        m_categoryIds.clear();
        
        QSqlQuery query(Database::instance().db());
        if(query.exec("SELECT id, name FROM categories ORDER BY name")) {
            while(query.next()) {
                m_categoryIds.append(query.value(0).toInt());
                m_categories.append(query.value(1).toString());
            }
        }
    }
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        auto *editor = new QComboBox(parent);
        editor->addItem("", QVariant());  // Empty option
        for(int i = 0; i < m_categories.size(); ++i) {
            editor->addItem(m_categories[i], m_categoryIds[i]);
        }
        return editor;
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        auto *comboBox = static_cast<QComboBox*>(editor);
        int categoryId = index.model()->data(index, Qt::EditRole).toInt();
        
        int idx = m_categoryIds.indexOf(categoryId);
        if (idx >= 0) {
            comboBox->setCurrentIndex(idx + 1);  // +1 because of empty option
        } else {
            comboBox->setCurrentIndex(0);  // Empty option
        }
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        auto *comboBox = static_cast<QComboBox*>(editor);
        QVariant categoryId = comboBox->currentData();
        model->setData(index, categoryId, Qt::EditRole);
    }
    
    QString displayText(const QVariant &value, const QLocale &locale) const override {
        int categoryId = value.toInt();
        int idx = m_categoryIds.indexOf(categoryId);
        if (idx >= 0) {
            return m_categories[idx];
        }
        return QString();
    }
    
private:
    mutable QList<int> m_categoryIds;
    mutable QStringList m_categories;
};

FixkostenPage::FixkostenPage(QWidget*parent):QWidget(parent){
    m_model=new QSqlTableModel(this,Database::instance().db()); 
    m_model->setTable("transactions"); 
    m_model->setEditStrategy(QSqlTableModel::OnFieldChange);
    m_model->setFilter("notes LIKE 'FIXKOSTEN:%'");
    m_model->select();
    
    // Set German column headers - hide notes column since it's auto-managed
    m_model->setHeaderData(1, Qt::Horizontal, "Datum");
    m_model->setHeaderData(2, Qt::Horizontal, "Beschreibung");
    m_model->setHeaderData(3, Qt::Horizontal, "Betrag (€)");
    m_model->setHeaderData(4, Qt::Horizontal, "Kategorie");
    m_model->setHeaderData(6, Qt::Horizontal, "Intervall");
    
    m_view=new QTableView(this); 
    m_view->setModel(m_model); 
    m_view->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_view->setAlternatingRowColors(true);
    
    // Set delegates for columns
    m_view->setItemDelegateForColumn(3, new CurrencyDelegate(this));  // Amount column
    m_view->setItemDelegateForColumn(4, new CategoryDelegate(this));  // Category column
    m_view->setItemDelegateForColumn(6, new FixkostenIntervalDelegate(this));  // Interval column
    
    // Hide unnecessary columns
    m_view->hideColumn(0);  // id
    // Show category_id column (4) with delegate
    m_view->hideColumn(5);  // person_id
    m_view->hideColumn(7);  // notes (auto-managed with FIXKOSTEN: prefix)
    m_view->hideColumn(8);  // created_at
    m_view->hideColumn(9);  // updated_at
    
    // Set header resize mode for even distribution
    m_view->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    // All columns stretch evenly since notes column is hidden
    
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