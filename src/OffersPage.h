#pragma once
#include <QWidget>
class QSqlTableModel; class QTableView; class QPushButton;
class OffersPage: public QWidget{
    Q_OBJECT
public:
    explicit OffersPage(QWidget*parent=nullptr);
private slots:
    void addRow();
    void removeRow();
private:
    QSqlTableModel*m_model;
    QTableView*m_view;
    QPushButton*m_add;
    QPushButton*m_del;
};
