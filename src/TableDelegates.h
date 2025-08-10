#pragma once
#include <QStyledItemDelegate>
#include <QLineEdit>
#include <QLocale>
#include <QDoubleValidator>
#include <QIntValidator>

class CurrencyDelegate : public QStyledItemDelegate {
public:
    CurrencyDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QString displayText(const QVariant &value, const QLocale &locale) const override {
        bool ok;
        double amount = value.toDouble(&ok);
        if (ok) {
            QLocale germanLocale(QLocale::German);
            return germanLocale.toString(amount, 'f', 2) + " €";
        }
        return value.toString();
    }
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        auto *editor = new QLineEdit(parent);
        editor->setValidator(new QDoubleValidator(-999999999.99, 999999999.99, 2, editor));
        return editor;
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        auto *lineEdit = static_cast<QLineEdit*>(editor);
        double value = index.model()->data(index, Qt::EditRole).toDouble();
        lineEdit->setText(QString::number(value, 'f', 2));
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        auto *lineEdit = static_cast<QLineEdit*>(editor);
        QString text = lineEdit->text();
        text.remove(" €");
        text.replace(",", ".");
        bool ok;
        double value = text.toDouble(&ok);
        if (ok) {
            model->setData(index, value, Qt::EditRole);
        }
    }
};

class PercentDelegate : public QStyledItemDelegate {
public:
    PercentDelegate(QObject *parent = nullptr) : QStyledItemDelegate(parent) {}
    
    QString displayText(const QVariant &value, const QLocale &locale) const override {
        bool ok;
        double percent = value.toDouble(&ok);
        if (ok) {
            return QString::number(percent, 'f', 0) + " %";
        }
        return value.toString();
    }
    
    QWidget *createEditor(QWidget *parent, const QStyleOptionViewItem &option,
                         const QModelIndex &index) const override {
        auto *editor = new QLineEdit(parent);
        editor->setValidator(new QIntValidator(0, 100, editor));
        return editor;
    }
    
    void setEditorData(QWidget *editor, const QModelIndex &index) const override {
        auto *lineEdit = static_cast<QLineEdit*>(editor);
        double value = index.model()->data(index, Qt::EditRole).toDouble();
        lineEdit->setText(QString::number(value, 'f', 0));
    }
    
    void setModelData(QWidget *editor, QAbstractItemModel *model,
                     const QModelIndex &index) const override {
        auto *lineEdit = static_cast<QLineEdit*>(editor);
        QString text = lineEdit->text();
        text.remove(" %");
        bool ok;
        double value = text.toDouble(&ok);
        if (ok) {
            model->setData(index, value, Qt::EditRole);
        }
    }
};