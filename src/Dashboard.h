#pragma once
#include <QWidget>
class QLabel; class QTableWidget; class QDoubleSpinBox; class QSpinBox; class QCheckBox;
QT_BEGIN_NAMESPACE class QChartView; class QBarSet; QT_END_NAMESPACE
class Dashboard: public QWidget{
    Q_OBJECT
public:
    explicit Dashboard(QWidget*parent=nullptr);
public slots:
    void refresh();
private slots:
    void saveStartBalance();
private:
    QLabel*m_sum{};
    QLabel*m_npv{};
    QLabel*m_irr{};
    QDoubleSpinBox*m_rate{};
    QDoubleSpinBox*m_startBalance{};
    QSpinBox*m_horizon{};
    QTableWidget*m_table{};
    QChartView*m_chart{};
    QCheckBox*m_includeOffers{};
    QCheckBox*m_includeInvoices{};
    QCheckBox*m_includeRecurring{};
};
